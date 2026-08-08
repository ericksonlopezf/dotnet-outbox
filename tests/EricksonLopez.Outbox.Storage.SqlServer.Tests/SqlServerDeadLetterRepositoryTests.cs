using System;
using System.Linq;
using System.Threading.Tasks;
using AutoFixture;
using AutoFixture.AutoNSubstitute;
using Dapper;
using EricksonLopez.Outbox;

using EricksonLopez.Outbox.Storage.SqlServer;
using AwesomeAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Xunit;

namespace EricksonLopez.Outbox.Tests;

public class SqlServerDeadLetterRepositoryTests : IClassFixture<SqlServerContainerFixture>, IAsyncLifetime
{
    private readonly SqlServerContainerFixture _fixture;
    private readonly IFixture _autoFixture;

    public SqlServerDeadLetterRepositoryTests(SqlServerContainerFixture fixture)
    {
        _fixture = fixture;
        _autoFixture = new Fixture().Customize(new AutoNSubstituteCustomization());
    }

    public async Task InitializeAsync()
    {
        using var connection = new SqlConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();

        const string schema = @"
            IF SCHEMA_ID('outbox') IS NULL
                EXEC('CREATE SCHEMA [outbox]');

            IF OBJECT_ID('outbox.messages_dead_letters', 'U') IS NULL
            BEGIN
                CREATE TABLE [outbox].[messages_dead_letters] (
                    id UNIQUEIDENTIFIER PRIMARY KEY,
                    original_message_id UNIQUEIDENTIFIER NOT NULL,
                    type NVARCHAR(255) NOT NULL,
                    payload NVARCHAR(MAX),
                    correlation_id NVARCHAR(255),
                    causation_id NVARCHAR(255),
                    headers_json NVARCHAR(MAX),
                    created_at DATETIMEOFFSET NOT NULL,
                    dead_lettered_at DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
                    retry_count INT NOT NULL DEFAULT 0,
                    reason NVARCHAR(MAX),
                    last_error NVARCHAR(MAX)
                );
            END
            ELSE
            BEGIN
                TRUNCATE TABLE [outbox].[messages_dead_letters];
            END";
        
        await connection.ExecuteAsync(schema);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private SqlServerDeadLetterRepository CreateSut()
    {
        var options = new Microsoft.Extensions.Options.OptionsMonitor<OutboxRuntimeOptions>(
            new Microsoft.Extensions.Options.OptionsFactory<OutboxRuntimeOptions>(
                Array.Empty<Microsoft.Extensions.Options.IConfigureOptions<OutboxRuntimeOptions>>(),
                Array.Empty<Microsoft.Extensions.Options.IPostConfigureOptions<OutboxRuntimeOptions>>()),
            Array.Empty<Microsoft.Extensions.Options.IOptionsChangeTokenSource<OutboxRuntimeOptions>>(),
            new Microsoft.Extensions.Options.OptionsCache<OutboxRuntimeOptions>());
        return new SqlServerDeadLetterRepository(() => new SqlConnection(_fixture.Container.GetConnectionString()), options);
    }

    [Fact]
    public async Task InsertAsync_Should_Persist_DeadLetterMessage()
    {
        var sut = CreateSut();
        var msg = _autoFixture.Create<DeadLetterMessage>() with { 
            Payload = "{}"u8.ToArray(),
            Headers = "{}"u8.ToArray()
        };

        await sut.InsertAsync(msg);

        await using var connection = new SqlConnection(_fixture.Container.GetConnectionString());
        var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM [outbox].[messages_dead_letters] WHERE id = @Id", new { msg.Id });
        count.Should().Be(1);
    }

    [Fact]
    public async Task InsertAsync_Should_Not_Throw_If_Already_Exists()
    {
        var sut = CreateSut();
        var msg = _autoFixture.Create<DeadLetterMessage>() with { 
            Payload = "{}"u8.ToArray(),
            Headers = "{}"u8.ToArray()
        };

        await sut.InsertAsync(msg);
        await sut.InsertAsync(msg); // Should ignore via IF NOT EXISTS

        await using var connection = new SqlConnection(_fixture.Container.GetConnectionString());
        var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM [outbox].[messages_dead_letters] WHERE id = @Id", new { msg.Id });
        count.Should().Be(1);
    }

    [Fact]
    public async Task InsertAsync_WithTransaction_Should_Use_Transaction()
    {
        var sut = CreateSut();
        var msg = _autoFixture.Create<DeadLetterMessage>() with { 
            Payload = "{}"u8.ToArray(),
            Headers = "{}"u8.ToArray()
        };

        await using var connection = new SqlConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        await using var tx = connection.BeginTransaction();

        await sut.InsertAsync(msg, new EricksonLopez.Outbox.Persistence.DbTransactionContext(tx));

        await tx.RollbackAsync();

        await using var newConn = new SqlConnection(_fixture.Container.GetConnectionString());
        var countAfterRollback = await newConn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM [outbox].[messages_dead_letters] WHERE id = @Id", new { msg.Id });
        countAfterRollback.Should().Be(0);
    }

    [Fact]
    public async Task InsertAsync_WithoutTransaction_Should_Open_Connection_And_Dispose()
    {
        var sut = CreateSut();
        var msg = _autoFixture.Create<DeadLetterMessage>() with { 
            Payload = "{}"u8.ToArray(),
            Headers = "{}"u8.ToArray()
        };

        // No transaction provided
        await sut.InsertAsync(msg);
        
        await using var connection = new SqlConnection(_fixture.Container.GetConnectionString());
        var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM [outbox].[messages_dead_letters] WHERE id = @Id", new { msg.Id });
        count.Should().Be(1);
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
            LastError = null
        };
        var msg2 = _autoFixture.Create<DeadLetterMessage>() with { 
            Payload = "{}"u8.ToArray(),
            Headers = "{}"u8.ToArray(),
            CorrelationId = "corr",
            CausationId = "caus",
            LastError = "err"
        };

        await sut.InsertAsync(msg1);
        await sut.InsertAsync(msg2);

        // Act
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
            Headers = "{}"u8.ToArray()
        };
        await sut.InsertAsync(msg);

        // Fetch using a date far in the future
        var results = await sut.GetAsync(after: DateTimeOffset.UtcNow.AddDays(1));
        
        // Should not contain the message we just inserted
        results.Should().NotContain(m => m.Id == msg.Id);
    }

    [Fact]
    public async Task DeleteAsync_Should_Remove_Message()
    {
        var sut = CreateSut();
        var msg = _autoFixture.Create<DeadLetterMessage>() with { 
            Payload = "{}"u8.ToArray(),
            Headers = "{}"u8.ToArray()
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
            Headers = "{}"u8.ToArray()
        };
        await sut.InsertAsync(msg);

        // Purge with a time in the future so that it purges the newly inserted message
        await sut.PurgeAsync(DateTimeOffset.UtcNow.AddDays(1));

        var results = await sut.GetAsync();
        results.Should().NotContain(m => m.Id == msg.Id);
    }
}




