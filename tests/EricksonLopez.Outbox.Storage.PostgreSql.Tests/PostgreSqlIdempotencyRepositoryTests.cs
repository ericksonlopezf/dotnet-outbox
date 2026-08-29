// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using AutoFixture;
using AutoFixture.AutoNSubstitute;
using AwesomeAssertions;
using Dapper;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Storage.PostgreSql;
using EricksonLopez.Result;
using Microsoft.Extensions.Options;
using Npgsql;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Storage.PostgreSql.Tests;

[Collection("PostgreSql")]
[Trait("Category", "Integration")]
public class PostgreSqlIdempotencyRepositoryTests : IAsyncLifetime
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
        await PostgreSqlTestDatabase.EnsureSchemaAsync(_dataSource);
    }

    public async Task DisposeAsync()
    {
        await using var connection = new NpgsqlConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand("TRUNCATE TABLE outbox.messages CASCADE", connection);
        await cmd.ExecuteNonQueryAsync();

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

    [Fact]
    public void Constructor_NullParameters_ThrowsArgumentNullException()
    {
        var optionsMonitor = Substitute.For<IOptionsMonitor<OutboxRuntimeOptions>>();
        optionsMonitor.CurrentValue.Returns(new OutboxRuntimeOptions { SchemaName = "outbox", TableName = "messages" });

        Action act1 = () => _ = new PostgreSqlIdempotencyRepository(null!, optionsMonitor);
        act1.Should().Throw<ArgumentNullException>().WithParameterName("dataSource");

        Action act2 = () => _ = new PostgreSqlIdempotencyRepository(_dataSource, null!);
        act2.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task TryInsertAsync_NullOrWhitespaceSchema_UsesPublicSchema(string? schema)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync("CREATE TABLE IF NOT EXISTS public.messages_idempotency (message_id text NOT NULL, consumer_id text NOT NULL, processed_at timestamp with time zone NOT NULL, CONSTRAINT pk_public_idempotency PRIMARY KEY (message_id, consumer_id));");

        var optionsMonitor = Substitute.For<IOptionsMonitor<OutboxRuntimeOptions>>();
        optionsMonitor.CurrentValue.Returns(new OutboxRuntimeOptions { SchemaName = schema!, TableName = "messages" });

        var repo = new PostgreSqlIdempotencyRepository(_dataSource, optionsMonitor);
        var record = new IdempotencyRecord(Guid.NewGuid().ToString(), "consumer_public_" + Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow);
        
        var result = await repo.TryInsertAsync(record);
        result.Should().BeTrue();
    }

    [Fact]
    public async Task TryInsertAsync_NonNpgsqlConnectionTransaction_ThrowsInvalidOperationException()
    {
        var sut = CreateSut();
        var record = new IdempotencyRecord(Guid.NewGuid().ToString(), "c1", DateTimeOffset.UtcNow);
        
        var mockTx = Substitute.For<IOutboxTransactionContext>();
        mockTx.Connection.Returns(Substitute.For<System.Data.Common.DbConnection>());

        Func<Task> act = async () => await sut.TryInsertAsync(record, mockTx);
        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.WithMessage("*NpgsqlConnection*");
    }

    [Fact]
    public async Task TryInsertAsync_WithoutTransaction_DisposesConnectionProperly()
    {
        var connString = _fixture.Container.GetConnectionString() + ";Maximum Pool Size=2;Timeout=2";
        await using var limitedDs = NpgsqlDataSource.Create(connString);

        var optionsMonitor = Substitute.For<IOptionsMonitor<OutboxRuntimeOptions>>();
        optionsMonitor.CurrentValue.Returns(new OutboxRuntimeOptions { SchemaName = "outbox", TableName = "messages" });

        var repo = new PostgreSqlIdempotencyRepository(limitedDs, optionsMonitor);

        // Run 5 sequential inserts. If connections are not disposed in finally, pool will exhaust and throw.
        for (int i = 0; i < 5; i++)
        {
            var record = new IdempotencyRecord(Guid.NewGuid().ToString(), $"consumer_pool_{i}", DateTimeOffset.UtcNow);
            var result = await repo.TryInsertAsync(record);
            result.Should().BeTrue();
        }
    }
}




