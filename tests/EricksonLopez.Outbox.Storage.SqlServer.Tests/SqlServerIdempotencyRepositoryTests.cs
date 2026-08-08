using System;
using System.Threading.Tasks;
using AutoFixture;
using Dapper;
using EricksonLopez.Outbox;
using AwesomeAssertions;
using AutoFixture.AutoNSubstitute;

using EricksonLopez.Outbox.Storage.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Xunit;

namespace EricksonLopez.Outbox.Tests;

public class SqlServerIdempotencyRepositoryTests : IClassFixture<SqlServerContainerFixture>, IAsyncLifetime
{
    private readonly SqlServerContainerFixture _fixture;
    private readonly IFixture _autoFixture;

    public SqlServerIdempotencyRepositoryTests(SqlServerContainerFixture fixture)
    {
        _fixture = fixture;
        _autoFixture = new Fixture().Customize(new AutoNSubstituteCustomization());
    }

    public async Task InitializeAsync()
    {
        using var connection = new SqlConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        
        await connection.ExecuteAsync(@"
            IF NOT EXISTS (SELECT * FROM sys.schemas WHERE name = 'outbox')
            BEGIN
                EXEC('CREATE SCHEMA [outbox]');
            END
        ");

        const string schema = @"
            IF OBJECT_ID('outbox.messages_idempotency', 'U') IS NULL
            BEGIN
                CREATE TABLE [outbox].[messages_idempotency] (
                    message_id NVARCHAR(255) NOT NULL,
                    consumer_id NVARCHAR(255) NOT NULL,
                    processed_at DATETIMEOFFSET NOT NULL,
                    PRIMARY KEY (message_id, consumer_id)
                );
            END
            ELSE
            BEGIN
                TRUNCATE TABLE [outbox].[messages_idempotency];
            END";
        
        await connection.ExecuteAsync(schema);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private SqlServerIdempotencyRepository CreateSut()
    {
        var options = new Microsoft.Extensions.Options.OptionsMonitor<OutboxRuntimeOptions>(
            new Microsoft.Extensions.Options.OptionsFactory<OutboxRuntimeOptions>(
                Array.Empty<Microsoft.Extensions.Options.IConfigureOptions<OutboxRuntimeOptions>>(),
                Array.Empty<Microsoft.Extensions.Options.IPostConfigureOptions<OutboxRuntimeOptions>>()),
            Array.Empty<Microsoft.Extensions.Options.IOptionsChangeTokenSource<OutboxRuntimeOptions>>(),
            new Microsoft.Extensions.Options.OptionsCache<OutboxRuntimeOptions>());
        return new SqlServerIdempotencyRepository(() => new SqlConnection(_fixture.Container.GetConnectionString()), options);
    }

    [Fact]
    public async Task TryInsertAsync_Should_Return_True_On_First_Insert()
    {
        var sut = CreateSut();
        var record = _autoFixture.Create<IdempotencyRecord>();

        var result = await sut.TryInsertAsync(record);
        result.Should().BeTrue();

        await using var connection = new SqlConnection(_fixture.Container.GetConnectionString());
        var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM [outbox].[messages_idempotency] WHERE message_id = @MessageId AND consumer_id = @ConsumerId", new { record.MessageId, record.ConsumerId });
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

        await using var connection = new SqlConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        await using var tx = connection.BeginTransaction();

        var result = await sut.TryInsertAsync(record, new EricksonLopez.Outbox.Persistence.DbTransactionContext(tx));
        result.Should().BeTrue();

        await tx.RollbackAsync();

        await using var newConn = new SqlConnection(_fixture.Container.GetConnectionString());
        var countAfterRollback = await newConn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM [outbox].[messages_idempotency] WHERE message_id = @MessageId", new { record.MessageId });
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

        await sut.PurgeExpiredRecordsAsync(now.AddDays(-1));

        await using var connection = new SqlConnection(_fixture.Container.GetConnectionString());
        var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM [outbox].[messages_idempotency]");
        count.Should().Be(2); // r2 and r3 remain
    }
}




