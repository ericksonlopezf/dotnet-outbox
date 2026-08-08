using System;
using System.Threading.Tasks;
using AutoFixture;
using AutoFixture.AutoNSubstitute;
using AwesomeAssertions;
using Dapper;
using EricksonLopez.Outbox.Storage.MySql;
using Microsoft.Extensions.Options;
using MySqlConnector;
using NSubstitute;
using Xunit;
using EricksonLopez.Outbox;

namespace EricksonLopez.Outbox.Tests;

public class MySqlIdempotencyRepositoryTests : IClassFixture<MySqlContainerFixture>, IAsyncLifetime
{
    private readonly MySqlContainerFixture _fixture;
    private readonly IFixture _autoFixture;
    private readonly EricksonLopez.Outbox.OutboxRuntimeOptions _options = new() { SchemaName = "testdb", TableName = "outbox_messages" };

    public MySqlIdempotencyRepositoryTests(MySqlContainerFixture fixture)
    {
        _fixture = fixture;
        _autoFixture = new Fixture().Customize(new AutoNSubstituteCustomization());
    }

    public async Task InitializeAsync()
    {
        using var connection = new MySqlConnection(_fixture.Container.GetConnectionString() + ";AllowLoadLocalInfile=true");
        await connection.OpenAsync();

        const string schema = @"
            CREATE TABLE IF NOT EXISTS outbox_messages_idempotency (
                message_id VARCHAR(36) NOT NULL,
                consumer_id VARCHAR(255) NOT NULL,
                processed_at DATETIME(6) NOT NULL,
                PRIMARY KEY (message_id, consumer_id)
            );
            TRUNCATE TABLE outbox_messages_idempotency;";
        
        await connection.ExecuteAsync(schema);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private MySqlIdempotencyRepository CreateSut()
    {
        var mockedOptions = Substitute.For<IOptionsMonitor<OutboxRuntimeOptions>>();
        mockedOptions.CurrentValue.Returns(_options);

        return new MySqlIdempotencyRepository(() => new MySqlConnection(_fixture.Container.GetConnectionString() + ";AllowLoadLocalInfile=true"), mockedOptions);
    }

    [Fact]
    public async Task TryInsertAsync_Should_Return_True_And_Persist_When_New()
    {
        var sut = CreateSut();
        var record = new IdempotencyRecord(Guid.NewGuid().ToString(), "consumer1", DateTimeOffset.UtcNow);

        var result = await sut.TryInsertAsync(record);

        result.Should().BeTrue();

        await using var connection = new MySqlConnection(_fixture.Container.GetConnectionString() + ";AllowLoadLocalInfile=true");
        var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM outbox_messages_idempotency WHERE message_id = @MessageId AND consumer_id = @ConsumerId", new { record.MessageId, record.ConsumerId });
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
        
        await using var connection = new MySqlConnection(_fixture.Container.GetConnectionString() + ";AllowLoadLocalInfile=true");
        var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM outbox_messages_idempotency WHERE message_id = @MessageId AND consumer_id = @ConsumerId", new { record.MessageId, record.ConsumerId });
        count.Should().Be(1);
    }

    [Fact]
    public async Task TryInsertAsync_WithTransaction_Should_Use_Transaction()
    {
        var sut = CreateSut();
        var record = new IdempotencyRecord(Guid.NewGuid().ToString(), "consumer1", DateTimeOffset.UtcNow);

        await using var connection = new MySqlConnection(_fixture.Container.GetConnectionString() + ";AllowLoadLocalInfile=true");
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();

        var result = await sut.TryInsertAsync(record, new EricksonLopez.Outbox.Persistence.DbTransactionContext(tx));
        result.Should().BeTrue();

        await tx.RollbackAsync();

        await using var newConn = new MySqlConnection(_fixture.Container.GetConnectionString() + ";AllowLoadLocalInfile=true");
        var count = await newConn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM outbox_messages_idempotency WHERE message_id = @MessageId AND consumer_id = @ConsumerId", new { record.MessageId, record.ConsumerId });
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

        await using var connection = new MySqlConnection(_fixture.Container.GetConnectionString() + ";AllowLoadLocalInfile=true");
        var staleCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM outbox_messages_idempotency WHERE message_id = @MessageId", new { staleRecord.MessageId });
        var freshCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM outbox_messages_idempotency WHERE message_id = @MessageId", new { freshRecord.MessageId });
        
        staleCount.Should().Be(0);
        freshCount.Should().Be(1);
    }
}
