using System;
using System.Linq;
using System.Threading.Tasks;
using AutoFixture;
using AutoFixture.AutoNSubstitute;
using AwesomeAssertions;
using Dapper;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Storage.MySql;
using MySqlConnector;
using Xunit;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace EricksonLopez.Outbox.Tests;

public class MySqlDeadLetterRepositoryTests : IClassFixture<MySqlContainerFixture>, IAsyncLifetime
{
    private readonly MySqlContainerFixture _fixture;
    private readonly IFixture _autoFixture;
    private readonly EricksonLopez.Outbox.OutboxRuntimeOptions _options = new() { SchemaName = "testdb", TableName = "outbox_messages" };

    public MySqlDeadLetterRepositoryTests(MySqlContainerFixture fixture)
    {
        _fixture = fixture;
        _autoFixture = new Fixture().Customize(new AutoNSubstituteCustomization());
    }

    public async Task InitializeAsync()
    {
        using var connection = new MySqlConnection(_fixture.Container.GetConnectionString() + ";AllowLoadLocalInfile=true");
        await connection.OpenAsync();

        const string schema = @"
            CREATE TABLE IF NOT EXISTS outbox_messages_dead_letters (
                id VARCHAR(36) PRIMARY KEY,
                original_message_id VARCHAR(36) NOT NULL,
                type VARCHAR(255) NOT NULL,
                payload LONGBLOB,
                correlation_id VARCHAR(255),
                causation_id VARCHAR(255),
                headers_json LONGBLOB,
                created_at DATETIME(6) NOT NULL,
                dead_lettered_at DATETIME(6) NOT NULL,
                retry_count INT NOT NULL DEFAULT 0,
                reason LONGTEXT,
                last_error LONGTEXT
            );
            TRUNCATE TABLE outbox_messages_dead_letters;";
        
        await connection.ExecuteAsync(schema);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private MySqlDeadLetterRepository CreateSut()
    {
        var options = new Microsoft.Extensions.Options.OptionsMonitor<OutboxRuntimeOptions>(
            new Microsoft.Extensions.Options.OptionsFactory<OutboxRuntimeOptions>(
                Array.Empty<Microsoft.Extensions.Options.IConfigureOptions<OutboxRuntimeOptions>>(),
                Array.Empty<Microsoft.Extensions.Options.IPostConfigureOptions<OutboxRuntimeOptions>>()),
            Array.Empty<Microsoft.Extensions.Options.IOptionsChangeTokenSource<OutboxRuntimeOptions>>(),
            new Microsoft.Extensions.Options.OptionsCache<OutboxRuntimeOptions>());
        // Since IOptionsMonitor doesn't easily let us mock the CurrentValue directly if it doesn't match the cache,
        // wait, I can just use NSubstitute to mock IOptionsMonitor!
        
        var mockedOptions = NSubstitute.Substitute.For<IOptionsMonitor<OutboxRuntimeOptions>>();
        mockedOptions.CurrentValue.Returns(_options);

        return new MySqlDeadLetterRepository(() => new MySqlConnection(_fixture.Container.GetConnectionString() + ";AllowLoadLocalInfile=true"), mockedOptions);
    }

    [Fact]
    public async Task InsertAsync_Should_Persist_DeadLetterMessage()
    {
        var sut = CreateSut();
        var msg = _autoFixture.Create<DeadLetterMessage>() with { 
            Payload = "{}"u8.ToArray(),
            Headers = "{}"u8.ToArray(),
            CreatedAt = DateTimeOffset.UtcNow,
            DeadLetteredAt = DateTimeOffset.UtcNow
        };

        await sut.InsertAsync(msg);

        await using var connection = new MySqlConnection(_fixture.Container.GetConnectionString() + ";AllowLoadLocalInfile=true");
        var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM outbox_messages_dead_letters WHERE id = @Id", new { Id = msg.Id.ToString() });
        count.Should().Be(1);
    }

    [Fact]
    public async Task InsertAsync_Should_Not_Throw_If_Already_Exists()
    {
        var sut = CreateSut();
        var msg = _autoFixture.Create<DeadLetterMessage>() with { 
            Payload = "{}"u8.ToArray(),
            Headers = "{}"u8.ToArray(),
            CreatedAt = DateTimeOffset.UtcNow,
            DeadLetteredAt = DateTimeOffset.UtcNow
        };

        await sut.InsertAsync(msg);
        await sut.InsertAsync(msg); // Should ignore via INSERT IGNORE

        await using var connection = new MySqlConnection(_fixture.Container.GetConnectionString() + ";AllowLoadLocalInfile=true");
        var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM outbox_messages_dead_letters WHERE id = @Id", new { Id = msg.Id.ToString() });
        count.Should().Be(1);
    }

    [Fact]
    public async Task InsertAsync_WithTransaction_Should_Use_Transaction()
    {
        var sut = CreateSut();
        var msg = _autoFixture.Create<DeadLetterMessage>() with { 
            Payload = "{}"u8.ToArray(),
            Headers = "{}"u8.ToArray(),
            CreatedAt = DateTimeOffset.UtcNow,
            DeadLetteredAt = DateTimeOffset.UtcNow
        };

        await using var connection = new MySqlConnection(_fixture.Container.GetConnectionString() + ";AllowLoadLocalInfile=true");
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();

        await sut.InsertAsync(msg, new EricksonLopez.Outbox.Persistence.DbTransactionContext(tx));

        await tx.RollbackAsync();

        await using var newConn = new MySqlConnection(_fixture.Container.GetConnectionString() + ";AllowLoadLocalInfile=true");
        var countAfterRollback = await newConn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM outbox_messages_dead_letters WHERE id = @Id", new { Id = msg.Id.ToString() });
        countAfterRollback.Should().Be(0);
    }

    [Fact]
    public async Task GetAsync_Should_Return_Messages_With_Null_Mapping()
    {
        var sut = CreateSut();
        var msg1 = _autoFixture.Create<DeadLetterMessage>() with { 
            Payload = "{}"u8.ToArray(),
            Headers = "{}"u8.ToArray(),
            CorrelationId = null,
            CausationId = null,
            LastError = null,
            CreatedAt = DateTimeOffset.UtcNow,
            DeadLetteredAt = DateTimeOffset.UtcNow
        };
        var msg2 = _autoFixture.Create<DeadLetterMessage>() with { 
            Payload = "{}"u8.ToArray(),
            Headers = "{}"u8.ToArray(),
            CorrelationId = "corr",
            CausationId = "caus",
            LastError = "err",
            CreatedAt = DateTimeOffset.UtcNow,
            DeadLetteredAt = DateTimeOffset.UtcNow
        };

        await sut.InsertAsync(msg1);
        await sut.InsertAsync(msg2);

        var results = await sut.GetAsync(10);
        
        results.Should().Contain(m => m.Id == msg1.Id);
        results.Should().Contain(m => m.Id == msg2.Id);

        var retrieved1 = results.FirstOrDefault(m => m.Id == msg1.Id)!;
        retrieved1.CorrelationId.Should().BeNull();
        retrieved1.CausationId.Should().BeNull();
        retrieved1.LastError.Should().BeNull();

        var retrieved2 = results.FirstOrDefault(m => m.Id == msg2.Id)!;
        retrieved2.CorrelationId.Should().Be("corr");
        retrieved2.CausationId.Should().Be("caus");
        retrieved2.LastError.Should().Be("err");
    }

    [Fact]
    public async Task GetAsync_Should_Filter_By_After_Date()
    {
        var sut = CreateSut();
        var msg = _autoFixture.Create<DeadLetterMessage>() with { 
            Payload = "{}"u8.ToArray(),
            Headers = "{}"u8.ToArray(),
            CreatedAt = DateTimeOffset.UtcNow,
            DeadLetteredAt = DateTimeOffset.UtcNow.AddDays(-2)
        };
        await sut.InsertAsync(msg);

        var results = await sut.GetAsync(after: DateTimeOffset.UtcNow.AddDays(1));
        results.Should().NotContain(m => m.Id == msg.Id);
    }

    [Fact]
    public async Task DeleteAsync_Should_Remove_Message()
    {
        var sut = CreateSut();
        var msg = _autoFixture.Create<DeadLetterMessage>() with { 
            Payload = "{}"u8.ToArray(),
            Headers = "{}"u8.ToArray(),
            CreatedAt = DateTimeOffset.UtcNow,
            DeadLetteredAt = DateTimeOffset.UtcNow
        };
        await sut.InsertAsync(msg);

        await sut.DeleteAsync(msg.Id);

        var results = await sut.GetAsync();
        results.Should().NotContain(m => m.Id == msg.Id);
    }

    [Fact]
    public async Task PurgeAsync_Should_Remove_Old_Messages()
    {
        var sut = CreateSut();
        var msg = _autoFixture.Create<DeadLetterMessage>() with { 
            Payload = "{}"u8.ToArray(),
            Headers = "{}"u8.ToArray(),
            CreatedAt = DateTimeOffset.UtcNow,
            DeadLetteredAt = DateTimeOffset.UtcNow.AddDays(-2)
        };
        await sut.InsertAsync(msg);

        await sut.PurgeAsync(DateTimeOffset.UtcNow.AddDays(1));

        var results = await sut.GetAsync();
        results.Should().NotContain(m => m.Id == msg.Id);
    }
}
