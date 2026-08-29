// Copyright © Erickson Lopez. MIT License.
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using AutoFixture;
using AutoFixture.AutoNSubstitute;
using AwesomeAssertions;
using Dapper;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Storage.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Storage.Sqlite.Tests;

public class SqliteIdempotencyRepositoryTests : IDisposable
{
    private readonly IFixture _autoFixture;
    private readonly string _connectionString;
    private readonly SqliteConnection _connection;
    private readonly OutboxRuntimeOptions _options = new() { TableName = "messages" };

    public SqliteIdempotencyRepositoryTests()
    {
        _autoFixture = new Fixture().Customize(new AutoNSubstituteCustomization());
        _connectionString = $"Data Source=outboxidemp_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _connection = new SqliteConnection(_connectionString);
        _connection.Open();

        SqliteTestDatabase.EnsureSchema(_connection);
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private SqliteIdempotencyRepository CreateSut(OutboxRuntimeOptions? customOptions = null)
    {
        var mockedOptions = Substitute.For<IOptionsMonitor<OutboxRuntimeOptions>>();
        mockedOptions.CurrentValue.Returns(customOptions ?? _options);
            
        return new SqliteIdempotencyRepository(() => new SqliteConnection(_connectionString), mockedOptions);
    }

    [Fact]
    public void Constructor_NullConnectionFactory_ThrowsArgumentNullException()
    {
        var mockedOptions = Substitute.For<IOptionsMonitor<OutboxRuntimeOptions>>();
        mockedOptions.CurrentValue.Returns(_options);

        Action act = () => { _ = new SqliteIdempotencyRepository(null!, mockedOptions); };
        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionFactory");
    }

    [Fact]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        Action act = () => { _ = new SqliteIdempotencyRepository(() => new SqliteConnection(), null!); };
        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public async Task TryInsertAsync_WithoutTransaction_DisposesConnection()
    {
        SqliteConnection? createdConn = null;
        var mockedOptions = Substitute.For<IOptionsMonitor<OutboxRuntimeOptions>>();
        mockedOptions.CurrentValue.Returns(_options);
        var sut = new SqliteIdempotencyRepository(() => {
            createdConn = new SqliteConnection(_connectionString);
            return createdConn;
        }, mockedOptions);

        var record = new IdempotencyRecord(Guid.NewGuid().ToString(), "c-disp", DateTimeOffset.UtcNow);
        var inserted = await sut.TryInsertAsync(record);
        inserted.Should().BeTrue();
        createdConn.Should().NotBeNull();
        createdConn!.State.Should().Be(ConnectionState.Closed);
    }

    [Fact]
    public async Task TryInsertAsync_WhenRecordIsNew_ReturnsTrueAndPersists()
    {
        var sut = CreateSut();
        var record = _autoFixture.Create<IdempotencyRecord>();

        var result = await sut.TryInsertAsync(record);

        result.Should().BeTrue();

        await using var connection = new SqliteConnection(_connectionString);
        var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM messages_idempotency WHERE message_id = @MessageId AND consumer_id = @ConsumerId", new { record.MessageId, record.ConsumerId });
        count.Should().Be(1);
    }

    [Fact]
    public async Task TryInsertAsync_WhenRecordAlreadyExists_ReturnsFalse()
    {
        var sut = CreateSut();
        var record = _autoFixture.Create<IdempotencyRecord>();

        await sut.TryInsertAsync(record);
        var result = await sut.TryInsertAsync(record);

        result.Should().BeFalse();
        
        await using var connection = new SqliteConnection(_connectionString);
        var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM messages_idempotency WHERE message_id = @MessageId AND consumer_id = @ConsumerId", new { record.MessageId, record.ConsumerId });
        count.Should().Be(1);
    }

    [Fact]
    public async Task TryInsertAsync_WhenTransactionRolledBack_RollsBackInsertion()
    {
        var sut = CreateSut();
        var record = _autoFixture.Create<IdempotencyRecord>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var tx = connection.BeginTransaction();

        var result = await sut.TryInsertAsync(record, new DbTransactionContext(tx));
        result.Should().BeTrue();

        await tx.RollbackAsync();

        await using var newConn = new SqliteConnection(_connectionString);
        var count = await newConn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM messages_idempotency WHERE message_id = @MessageId AND consumer_id = @ConsumerId", new { record.MessageId, record.ConsumerId });
        count.Should().Be(0);
    }

    [Fact]
    public async Task PurgeExpiredRecordsAsync_WhenExpiredRecordsExist_DeletesOlderThanCutoff()
    {
        var sut = CreateSut();
        var staleRecord = new IdempotencyRecord(Guid.NewGuid().ToString(), "consumer1", DateTimeOffset.UtcNow.AddDays(-2));
        var freshRecord = new IdempotencyRecord(Guid.NewGuid().ToString(), "consumer1", DateTimeOffset.UtcNow);

        await sut.TryInsertAsync(staleRecord);
        await sut.TryInsertAsync(freshRecord);

        await sut.PurgeExpiredRecordsAsync(DateTimeOffset.UtcNow.AddDays(-1));

        await using var connection = new SqliteConnection(_connectionString);
        var staleCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM messages_idempotency WHERE message_id = @MessageId", new { staleRecord.MessageId });
        var freshCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM messages_idempotency WHERE message_id = @MessageId", new { freshRecord.MessageId });
        
        staleCount.Should().Be(0);
        freshCount.Should().Be(1);
    }
}
