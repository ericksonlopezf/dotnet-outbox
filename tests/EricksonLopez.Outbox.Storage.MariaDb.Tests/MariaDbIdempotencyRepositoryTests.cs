// Copyright © Erickson Lopez. MIT License.
using System;
using System.Data;
using System.Threading.Tasks;
using AutoFixture;
using AutoFixture.AutoNSubstitute;
using AwesomeAssertions;
using Dapper;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Storage.MariaDb;
using Microsoft.Extensions.Options;
using MySqlConnector;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Storage.MariaDb.Tests;

[Collection("MariaDb")]
[Trait("Category", "Integration")]
public class MariaDbIdempotencyRepositoryTests : IAsyncLifetime
{
    private readonly MariaDbContainerFixture _fixture;
    private readonly IFixture _autoFixture;
    private readonly OutboxRuntimeOptions _options = new() { SchemaName = "", TableName = "outbox_messages" };

    public MariaDbIdempotencyRepositoryTests(MariaDbContainerFixture fixture)
    {
        _fixture = fixture;
        _autoFixture = new Fixture().Customize(new AutoNSubstituteCustomization());
    }

    public async Task InitializeAsync()
    {
        await MariaDbTestDatabase.EnsureSchemaAsync(_fixture.Container.GetConnectionString());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private MariaDbIdempotencyRepository CreateSut(OutboxRuntimeOptions? customOptions = null)
    {
        var mockedOptions = Substitute.For<IOptionsMonitor<OutboxRuntimeOptions>>();
        mockedOptions.CurrentValue.Returns(customOptions ?? _options);

        return new MariaDbIdempotencyRepository(
            () => new MySqlConnection(_fixture.Container.GetConnectionString()),
            mockedOptions);
    }

    [Fact]
    public void Constructor_NullConnectionFactory_ThrowsArgumentNullException()
    {
        var mockedOptions = Substitute.For<IOptionsMonitor<OutboxRuntimeOptions>>();
        mockedOptions.CurrentValue.Returns(_options);

        Action act = () => { _ = new MariaDbIdempotencyRepository(null!, mockedOptions); };
        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionFactory");
    }

    [Fact]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        Action act = () => { _ = new MariaDbIdempotencyRepository(() => new MySqlConnection(), null!); };
        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public async Task Operations_WithNonExistentSchema_ThrowsMySqlExceptionContainingSchemaName()
    {
        var custom = new OutboxRuntimeOptions { SchemaName = "non_existent_schema_xyz", TableName = "outbox_messages" };
        var sut = CreateSut(custom);
        var record = new IdempotencyRecord(Guid.NewGuid().ToString(), "c-test", DateTimeOffset.UtcNow);
        Func<Task> act = async () => await sut.TryInsertAsync(record);
        var ex = await act.Should().ThrowAsync<MySqlException>();
        ex.Which.Message.Should().Contain("non_existent_schema_xyz");
    }

    [Fact]
    public async Task TryInsertAsync_WithoutTransaction_DisposesConnection()
    {
        MySqlConnection? createdConn = null;
        var mockedOptions = Substitute.For<IOptionsMonitor<OutboxRuntimeOptions>>();
        mockedOptions.CurrentValue.Returns(_options);
        var sut = new MariaDbIdempotencyRepository(() => {
            createdConn = new MySqlConnection(_fixture.Container.GetConnectionString());
            return createdConn;
        }, mockedOptions);

        var record = new IdempotencyRecord(Guid.NewGuid().ToString(), "c-disp", DateTimeOffset.UtcNow);
        var inserted = await sut.TryInsertAsync(record);
        inserted.Should().BeTrue();
        createdConn.Should().NotBeNull();
        createdConn!.State.Should().Be(ConnectionState.Closed);
    }

    [Fact]
    public async Task TryInsertAsync_Should_Return_True_And_Persist_When_New()
    {
        var sut = CreateSut();
        var record = new IdempotencyRecord(Guid.NewGuid().ToString(), "consumer1", DateTimeOffset.UtcNow);

        var result = await sut.TryInsertAsync(record);

        result.Should().BeTrue();

        await using var connection = new MySqlConnection(_fixture.Container.GetConnectionString());
        var count = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM outbox_messages_idempotency WHERE message_id = @MessageId AND consumer_id = @ConsumerId",
            new { record.MessageId, record.ConsumerId });
        count.Should().Be(1);
    }

    [Fact]
    public async Task TryInsertAsync_Should_Return_False_When_Already_Exists()
    {
        var sut = CreateSut();
        var record = new IdempotencyRecord(Guid.NewGuid().ToString(), "consumer1", DateTimeOffset.UtcNow);

        await sut.TryInsertAsync(record);
        var result = await sut.TryInsertAsync(record);

        result.Should().BeFalse();

        await using var connection = new MySqlConnection(_fixture.Container.GetConnectionString());
        var count = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM outbox_messages_idempotency WHERE message_id = @MessageId AND consumer_id = @ConsumerId",
            new { record.MessageId, record.ConsumerId });
        count.Should().Be(1);
    }

    [Fact]
    public async Task TryInsertAsync_WithTransaction_Should_Use_Transaction()
    {
        var sut = CreateSut();
        var record = new IdempotencyRecord(Guid.NewGuid().ToString(), "consumer1", DateTimeOffset.UtcNow);

        await using var connection = new MySqlConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();

        var result = await sut.TryInsertAsync(record, new DbTransactionContext(tx));
        result.Should().BeTrue();

        await tx.RollbackAsync();

        await using var newConn = new MySqlConnection(_fixture.Container.GetConnectionString());
        var count = await newConn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM outbox_messages_idempotency WHERE message_id = @MessageId AND consumer_id = @ConsumerId",
            new { record.MessageId, record.ConsumerId });
        count.Should().Be(0);
    }

    [Fact]
    public async Task PurgeExpiredRecordsAsync_Should_Delete_Records_Older_Than_Given_Date()
    {
        var sut = CreateSut();
        var staleRecord = new IdempotencyRecord(Guid.NewGuid().ToString(), "consumer1", DateTimeOffset.UtcNow.AddDays(-2));
        var freshRecord = new IdempotencyRecord(Guid.NewGuid().ToString(), "consumer1", DateTimeOffset.UtcNow);

        await sut.TryInsertAsync(staleRecord);
        await sut.TryInsertAsync(freshRecord);

        await sut.PurgeExpiredRecordsAsync(DateTimeOffset.UtcNow.AddDays(-1));

        await using var connection = new MySqlConnection(_fixture.Container.GetConnectionString());
        var staleCount = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM outbox_messages_idempotency WHERE message_id = @MessageId",
            new { staleRecord.MessageId });
        var freshCount = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM outbox_messages_idempotency WHERE message_id = @MessageId",
            new { freshRecord.MessageId });

        staleCount.Should().Be(0);
        freshCount.Should().Be(1);
    }
}
