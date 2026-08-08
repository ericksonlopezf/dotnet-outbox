using System;
using System.Threading.Tasks;
using AutoFixture;
using AutoFixture.AutoNSubstitute;
using AwesomeAssertions;
using Dapper;
using EricksonLopez.Outbox.Storage.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using Xunit;
using EricksonLopez.Outbox;

namespace EricksonLopez.Outbox.Tests;

public class SqliteIdempotencyRepositoryTests : IDisposable
{
    private readonly IFixture _autoFixture;
    private readonly SqliteConnection _connection;

    public SqliteIdempotencyRepositoryTests()
    {
        _autoFixture = new Fixture().Customize(new AutoNSubstituteCustomization());
        _connection = new SqliteConnection("Data Source=outboxidemptests;Mode=Memory;Cache=Shared");
        _connection.Open();

        const string schema = @"
            CREATE TABLE IF NOT EXISTS messages_idempotency (
                message_id TEXT NOT NULL,
                consumer_id TEXT NOT NULL,
                processed_at TEXT NOT NULL,
                PRIMARY KEY (message_id, consumer_id)
            );
            DELETE FROM messages_idempotency;";
        
        _connection.Execute(schema);
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private static SqliteIdempotencyRepository CreateSut()
    {
        var options = new Microsoft.Extensions.Options.OptionsMonitor<OutboxRuntimeOptions>(
            new Microsoft.Extensions.Options.OptionsFactory<OutboxRuntimeOptions>(
                Array.Empty<Microsoft.Extensions.Options.IConfigureOptions<OutboxRuntimeOptions>>(),
                Array.Empty<Microsoft.Extensions.Options.IPostConfigureOptions<OutboxRuntimeOptions>>()),
            Array.Empty<Microsoft.Extensions.Options.IOptionsChangeTokenSource<OutboxRuntimeOptions>>(),
            new Microsoft.Extensions.Options.OptionsCache<OutboxRuntimeOptions>());
            
        return new SqliteIdempotencyRepository(() => new SqliteConnection("Data Source=outboxidemptests;Mode=Memory;Cache=Shared"), options);
    }

    [Fact]
    public async Task TryInsertAsync_Should_Return_True_And_Persist_When_New()
    {
        var sut = CreateSut();
        var record = _autoFixture.Create<IdempotencyRecord>();

        var result = await sut.TryInsertAsync(record);

        result.Should().BeTrue();

        await using var connection = new SqliteConnection("Data Source=outboxidemptests;Mode=Memory;Cache=Shared");
        var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM messages_idempotency WHERE message_id = @MessageId AND consumer_id = @ConsumerId", new { record.MessageId, record.ConsumerId });
        count.Should().Be(1);
    }

    [Fact]
    public async Task TryInsertAsync_Should_Return_False_When_Already_Exists()
    {
        var sut = CreateSut();
        var record = _autoFixture.Create<IdempotencyRecord>();

        await sut.TryInsertAsync(record);
        var result = await sut.TryInsertAsync(record);

        result.Should().BeFalse();
        
        await using var connection = new SqliteConnection("Data Source=outboxidemptests;Mode=Memory;Cache=Shared");
        var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM messages_idempotency WHERE message_id = @MessageId AND consumer_id = @ConsumerId", new { record.MessageId, record.ConsumerId });
        count.Should().Be(1);
    }

    [Fact]
    public async Task TryInsertAsync_WithTransaction_Should_Use_Transaction()
    {
        var sut = CreateSut();
        var record = _autoFixture.Create<IdempotencyRecord>();

        await using var connection = new SqliteConnection("Data Source=outboxidemptests;Mode=Memory;Cache=Shared");
        await connection.OpenAsync();
        await using var tx = connection.BeginTransaction();

        var result = await sut.TryInsertAsync(record, new EricksonLopez.Outbox.Persistence.DbTransactionContext(tx));
        result.Should().BeTrue();

        await tx.RollbackAsync();

        await using var newConn = new SqliteConnection("Data Source=outboxidemptests;Mode=Memory;Cache=Shared");
        var count = await newConn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM messages_idempotency WHERE message_id = @MessageId AND consumer_id = @ConsumerId", new { record.MessageId, record.ConsumerId });
        count.Should().Be(0);
    }

    [Fact]
    public async Task PurgeExpiredRecordsAsync_Should_Delete_Records_Older_Than_Given_Date()
    {
        var sut = CreateSut();
        var staleRecord = _autoFixture.Create<IdempotencyRecord>() with { ProcessedAt = DateTimeOffset.UtcNow.AddDays(-2) };
        var freshRecord = _autoFixture.Create<IdempotencyRecord>() with { ProcessedAt = DateTimeOffset.UtcNow };

        await sut.TryInsertAsync(staleRecord);
        await sut.TryInsertAsync(freshRecord);

        await sut.PurgeExpiredRecordsAsync(DateTimeOffset.UtcNow.AddDays(-1));

        await using var connection = new SqliteConnection("Data Source=outboxidemptests;Mode=Memory;Cache=Shared");
        var staleCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM messages_idempotency WHERE message_id = @MessageId", new { staleRecord.MessageId });
        var freshCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM messages_idempotency WHERE message_id = @MessageId", new { freshRecord.MessageId });
        
        staleCount.Should().Be(0);
        freshCount.Should().Be(1);
    }
}
