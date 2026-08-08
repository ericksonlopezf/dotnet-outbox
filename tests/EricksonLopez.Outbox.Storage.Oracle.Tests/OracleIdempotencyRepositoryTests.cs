using System;
using System.Threading.Tasks;
using AutoFixture;
using Dapper;
using EricksonLopez.Outbox;
using AwesomeAssertions;
using AutoFixture.AutoNSubstitute;
using EricksonLopez.Outbox.Storage.Oracle;
using Oracle.ManagedDataAccess.Client;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Tests;

public class OracleIdempotencyRepositoryTests : IClassFixture<OracleContainerFixture>, IAsyncLifetime
{
    private readonly OracleContainerFixture _fixture;
    private readonly IFixture _autoFixture;

    public OracleIdempotencyRepositoryTests(OracleContainerFixture fixture)
    {
        _fixture = fixture;
        _autoFixture = new Fixture().Customize(new AutoNSubstituteCustomization());
    }

    public async Task InitializeAsync()
    {
        using var connection = new OracleConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();

        const string schema = @"
            BEGIN
                EXECUTE IMMEDIATE 'CREATE TABLE ""MESSAGES_IDEMPOTENCY"" (
                    message_id VARCHAR2(255) NOT NULL,
                    consumer_id VARCHAR2(255) NOT NULL,
                    processed_at TIMESTAMP(6) WITH TIME ZONE NOT NULL,
                    PRIMARY KEY (message_id, consumer_id)
                )';
            EXCEPTION
                WHEN OTHERS THEN
                    IF SQLCODE != -955 THEN
                        RAISE;
                    END IF;
            END;";
            
        await connection.ExecuteAsync(schema);
        await connection.ExecuteAsync("TRUNCATE TABLE \"MESSAGES_IDEMPOTENCY\"");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private OracleIdempotencyRepository CreateSut()
    {
        var optionsMonitor = Substitute.For<IOptionsMonitor<OutboxRuntimeOptions>>();
        optionsMonitor.CurrentValue.Returns(new OutboxRuntimeOptions { SchemaName = string.Empty, TableName = "messages" });
        return new OracleIdempotencyRepository(() => new OracleConnection(_fixture.Container.GetConnectionString()), optionsMonitor);
    }

    [Fact]
    public async Task TryInsertAsync_Should_Return_True_On_First_Insert()
    {
        var sut = CreateSut();
        var record = _autoFixture.Create<IdempotencyRecord>();

        var result = await sut.TryInsertAsync(record);
        result.Should().BeTrue();

        await using var connection = new OracleConnection(_fixture.Container.GetConnectionString());
        var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM \"MESSAGES_IDEMPOTENCY\" WHERE message_id = :MessageId AND consumer_id = :ConsumerId", new { record.MessageId, record.ConsumerId });
        count.Should().Be(1);
    }

    [Fact]
    public async Task TryInsertAsync_Should_Return_False_On_Duplicate()
    {
        var sut = CreateSut();
        var record = _autoFixture.Create<IdempotencyRecord>();

        var first = await sut.TryInsertAsync(record);
        var second = await sut.TryInsertAsync(record);

        first.Should().BeTrue();
        second.Should().BeFalse();
    }

    [Fact]
    public async Task TryInsertAsync_WithTransaction_Should_Enlist()
    {
        var sut = CreateSut();
        var record = _autoFixture.Create<IdempotencyRecord>();

        await using var connection = new OracleConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        await using var tx = connection.BeginTransaction();

        var result = await sut.TryInsertAsync(record, new EricksonLopez.Outbox.Persistence.DbTransactionContext(tx));
        result.Should().BeTrue();

        await tx.RollbackAsync();

        await using var newConn = new OracleConnection(_fixture.Container.GetConnectionString());
        var countAfterRollback = await newConn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM \"MESSAGES_IDEMPOTENCY\" WHERE message_id = :MessageId", new { record.MessageId });
        countAfterRollback.Should().Be(0);
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

        await sut.PurgeExpiredRecordsAsync(now.AddDays(-1.5));

        await using var connection = new OracleConnection(_fixture.Container.GetConnectionString());
        var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM \"MESSAGES_IDEMPOTENCY\"");
        count.Should().Be(2); // r2 and r3 remain
    }
}
