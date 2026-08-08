using System;
using System.Threading;
using System.Threading.Tasks;
using AutoFixture;
using Dapper;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Storage.PostgreSql;
using AwesomeAssertions;
using AutoFixture.AutoNSubstitute;
using Microsoft.Extensions.Options;
using NSubstitute;
using Npgsql;
using Xunit;

namespace EricksonLopez.Outbox.Tests;

public class PostgreSqlIdempotencyRepositoryTests : IClassFixture<PostgreSqlContainerFixture>, IAsyncLifetime
{
    private readonly PostgreSqlContainerFixture _fixture;
    private readonly IFixture _autoFixture;
    private NpgsqlDataSource _dataSource = null!;

    public PostgreSqlIdempotencyRepositoryTests(PostgreSqlContainerFixture fixture)
    {
        _fixture = fixture;
        _autoFixture = new Fixture().Customize(new AutoNSubstituteCustomization());
    }

    public async Task InitializeAsync()
    {
        _dataSource = NpgsqlDataSource.Create(_fixture.Container.GetConnectionString());
        await using var connection = await _dataSource.OpenConnectionAsync();
        
        await connection.ExecuteAsync(@"
            CREATE SCHEMA IF NOT EXISTS outbox;
            
            CREATE TABLE IF NOT EXISTS outbox.messages_idempotency (
                message_id VARCHAR(255) NOT NULL,
                consumer_id VARCHAR(255) NOT NULL,
                processed_at TIMESTAMPTZ NOT NULL,
                PRIMARY KEY (message_id, consumer_id)
            );
            
            TRUNCATE TABLE outbox.messages_idempotency;
        ");
    }

    public async Task DisposeAsync()
    {
        if (_dataSource != null)
        {
            await _dataSource.DisposeAsync();
        }
    }

    private PostgreSqlIdempotencyRepository CreateSut()
    {
        var optionsMonitor = Substitute.For<IOptionsMonitor<OutboxRuntimeOptions>>();
        optionsMonitor.CurrentValue.Returns(new OutboxRuntimeOptions { SchemaName = "outbox", TableName = "messages" });
        return new PostgreSqlIdempotencyRepository(_dataSource, optionsMonitor);
    }

    [Fact]
    public async Task TryInsertAsync_Should_Return_True_On_First_Insert()
    {
        var sut = CreateSut();
        var record = _autoFixture.Create<IdempotencyRecord>() with { ProcessedAt = DateTimeOffset.UtcNow };

        var result = await sut.TryInsertAsync(record);
        result.Should().BeTrue();

        await using var connection = await _dataSource.OpenConnectionAsync();
        var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM outbox.messages_idempotency WHERE message_id = @MessageId AND consumer_id = @ConsumerId", new { record.MessageId, record.ConsumerId });
        count.Should().Be(1);
    }

    [Fact]
    public async Task TryInsertAsync_Should_Return_False_On_Duplicate()
    {
        var sut = CreateSut();
        var record = _autoFixture.Create<IdempotencyRecord>() with { ProcessedAt = DateTimeOffset.UtcNow };

        var first = await sut.TryInsertAsync(record);
        var second = await sut.TryInsertAsync(record);

        first.Should().BeTrue();
        second.Should().BeFalse();
    }

    [Fact]
    public async Task TryInsertAsync_WithTransaction_Should_Enlist()
    {
        var sut = CreateSut();
        var record = _autoFixture.Create<IdempotencyRecord>() with { ProcessedAt = DateTimeOffset.UtcNow };

        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var tx = await connection.BeginTransactionAsync();

        var result = await sut.TryInsertAsync(record, new EricksonLopez.Outbox.Persistence.DbTransactionContext(tx));
        result.Should().BeTrue();

        await tx.RollbackAsync();

        await using var newConn = await _dataSource.OpenConnectionAsync();
        var countAfterRollback = await newConn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM outbox.messages_idempotency WHERE message_id = @MessageId", new { record.MessageId });
        countAfterRollback.Should().Be(0);
    }
    
    [Fact]
    public async Task TryInsertAsync_WithCanceledToken_Should_Throw_And_Dispose()
    {
        var sut = CreateSut();
        var record = _autoFixture.Create<IdempotencyRecord>() with { ProcessedAt = DateTimeOffset.UtcNow };
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        
        Func<Task> act = async () => await sut.TryInsertAsync(record, cancellationToken: cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task PurgeExpiredRecordsAsync_Should_Delete_Old_Records()
    {
        var sut = CreateSut();
        var now = DateTimeOffset.UtcNow;
        
        var r1 = new IdempotencyRecord(Guid.NewGuid().ToString(), "c1", now.AddDays(-2));
        var r2 = new IdempotencyRecord(Guid.NewGuid().ToString(), "c2", now.AddDays(-1));
        var r3 = new IdempotencyRecord(Guid.NewGuid().ToString(), "c3", now);
        
        await sut.TryInsertAsync(r1);
        await sut.TryInsertAsync(r2);
        await sut.TryInsertAsync(r3);

        await sut.PurgeExpiredRecordsAsync(now.AddDays(-1));

        await using var connection = await _dataSource.OpenConnectionAsync();
        var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM outbox.messages_idempotency");
        count.Should().Be(2);
    }
}
