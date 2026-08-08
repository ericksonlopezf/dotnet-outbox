using System;
using System.Linq;
using System.Threading.Tasks;
using AutoFixture;
using AutoFixture.AutoNSubstitute;
using AwesomeAssertions;
using Dapper;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Storage.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Xunit;

namespace EricksonLopez.Outbox.Tests;

public class SqliteDeadLetterRepositoryTests : IDisposable
{
    private readonly IFixture _autoFixture;
    private readonly SqliteConnection _connection;

    public SqliteDeadLetterRepositoryTests()
    {
        _autoFixture = new Fixture().Customize(new AutoNSubstituteCustomization());
        _connection = new SqliteConnection("Data Source=outboxdltests;Mode=Memory;Cache=Shared");
        _connection.Open();

        const string schema = @"
            CREATE TABLE IF NOT EXISTS messages_dead_letters (
                id TEXT PRIMARY KEY,
                original_message_id TEXT NOT NULL,
                type TEXT NOT NULL,
                payload BLOB,
                correlation_id TEXT,
                causation_id TEXT,
                headers_json BLOB,
                created_at TEXT NOT NULL,
                dead_lettered_at TEXT NOT NULL,
                retry_count INTEGER NOT NULL DEFAULT 0,
                reason TEXT,
                last_error TEXT
            );
            DELETE FROM messages_dead_letters;";
        
        _connection.Execute(schema);
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private static SqliteDeadLetterRepository CreateSut()
    {
        var options = new Microsoft.Extensions.Options.OptionsMonitor<OutboxRuntimeOptions>(
            new Microsoft.Extensions.Options.OptionsFactory<OutboxRuntimeOptions>(
                Array.Empty<Microsoft.Extensions.Options.IConfigureOptions<OutboxRuntimeOptions>>(),
                Array.Empty<Microsoft.Extensions.Options.IPostConfigureOptions<OutboxRuntimeOptions>>()),
            Array.Empty<Microsoft.Extensions.Options.IOptionsChangeTokenSource<OutboxRuntimeOptions>>(),
            new Microsoft.Extensions.Options.OptionsCache<OutboxRuntimeOptions>());
            
        return new SqliteDeadLetterRepository(() => new SqliteConnection("Data Source=outboxdltests;Mode=Memory;Cache=Shared"), options);
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

        await using var connection = new SqliteConnection("Data Source=outboxdltests;Mode=Memory;Cache=Shared");
        var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM messages_dead_letters WHERE id = @Id", new { Id = msg.Id.ToString() });
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
        await sut.InsertAsync(msg); // Should ignore via INSERT OR IGNORE

        await using var connection = new SqliteConnection("Data Source=outboxdltests;Mode=Memory;Cache=Shared");
        var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM messages_dead_letters WHERE id = @Id", new { Id = msg.Id.ToString() });
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

        await using var connection = new SqliteConnection("Data Source=outboxdltests;Mode=Memory;Cache=Shared");
        await connection.OpenAsync();
        await using var tx = connection.BeginTransaction();

        await sut.InsertAsync(msg, new EricksonLopez.Outbox.Persistence.DbTransactionContext(tx));

        await tx.RollbackAsync();

        await using var newConn = new SqliteConnection("Data Source=outboxdltests;Mode=Memory;Cache=Shared");
        var countAfterRollback = await newConn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM messages_dead_letters WHERE id = @Id", new { Id = msg.Id.ToString() });
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
            Headers = "{}"u8.ToArray(),
            DeadLetteredAt = DateTimeOffset.UtcNow.AddDays(-2)
        };
        await sut.InsertAsync(msg);

        await sut.PurgeAsync(DateTimeOffset.UtcNow.AddDays(1));

        var results = await sut.GetAsync();
        results.Should().NotContain(m => m.Id == msg.Id);
    }
}
