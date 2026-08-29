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
using EricksonLopez.Outbox.Storage.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Storage.SqlServer.Tests;

[Collection("SqlServer")]
[Trait("Category", "Integration")]
public class SqlServerIdempotencyRepositoryTests : IAsyncLifetime
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
        await SqlServerTestDatabase.EnsureSchemaAsync(_fixture.Container.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        await using var connection = new SqlConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        var options = new OutboxRuntimeOptions();
        await connection.ExecuteAsync($"TRUNCATE TABLE [outbox].[{options.TableName}_idempotency]");
    }

    private SqlServerIdempotencyRepository CreateSut(OutboxRuntimeOptions? customOptions = null)
    {
        var opt = customOptions ?? new OutboxRuntimeOptions();
        var options = new OptionsMonitor<OutboxRuntimeOptions>(
            new OptionsFactory<OutboxRuntimeOptions>(
                new[] { new ConfigureOptions<OutboxRuntimeOptions>(o => {
                    o.SchemaName = opt.SchemaName;
                    o.TableName = opt.TableName;
                }) },
                Array.Empty<IPostConfigureOptions<OutboxRuntimeOptions>>()),
            Array.Empty<IOptionsChangeTokenSource<OutboxRuntimeOptions>>(),
            new OptionsCache<OutboxRuntimeOptions>());
        return new SqlServerIdempotencyRepository(() => new SqlConnection(_fixture.Container.GetConnectionString()), options);
    }

    [Fact]
    public void Constructor_NullConnectionFactory_ThrowsArgumentNullException()
    {
        var options = new OptionsMonitor<OutboxRuntimeOptions>(
            new OptionsFactory<OutboxRuntimeOptions>(Array.Empty<IConfigureOptions<OutboxRuntimeOptions>>(), Array.Empty<IPostConfigureOptions<OutboxRuntimeOptions>>()),
            Array.Empty<IOptionsChangeTokenSource<OutboxRuntimeOptions>>(),
            new OptionsCache<OutboxRuntimeOptions>());

        Action act = () => { _ = new SqlServerIdempotencyRepository(null!, options); };
        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionFactory");
    }

    [Fact]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        Action act = () => { _ = new SqlServerIdempotencyRepository(() => new SqlConnection(), null!); };
        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("dbo")]
    [InlineData("outbox")]
    public async Task Operations_WithConfiguredSchema_TargetsConfiguredSchemaTable(string? schemaName)
    {
        var custom = new OutboxRuntimeOptions { SchemaName = schemaName!, TableName = "messages" };
        var sut = CreateSut(custom);
        var record = new IdempotencyRecord(Guid.NewGuid().ToString(), "c-test", DateTimeOffset.UtcNow);
        var inserted = await sut.TryInsertAsync(record);
        inserted.Should().BeTrue();
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

        var result = await sut.TryInsertAsync(record, new DbTransactionContext(tx));
        result.Should().BeTrue();

        await tx.RollbackAsync();

        await using var newConn = new SqlConnection(_fixture.Container.GetConnectionString());
        var countAfterRollback = await newConn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM [outbox].[messages_idempotency] WHERE message_id = @MessageId", new { record.MessageId });
        countAfterRollback.Should().Be(0);
    }

    [Fact]
    public async Task TryInsertAsync_WithInvalidTransactionConnection_ThrowsInvalidOperationException()
    {
        var sut = CreateSut();
        var record = _autoFixture.Create<IdempotencyRecord>();

        var invalidTx = Substitute.For<IOutboxTransactionContext>();
        invalidTx.Connection.Returns(Substitute.For<IDbConnection>());

        Func<Task> act = async () => await sut.TryInsertAsync(record, invalidTx);
        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.WithMessage("Connection must be a SqlConnection");
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

    [Fact]
    public async Task Operations_WithoutTransaction_DisposeConnectionProperly()
    {
        var builder = new SqlConnectionStringBuilder(_fixture.Container.GetConnectionString())
        {
            MaxPoolSize = 2,
            ConnectTimeout = 2
        };

        var options = new OptionsMonitor<OutboxRuntimeOptions>(
            new OptionsFactory<OutboxRuntimeOptions>(Array.Empty<IConfigureOptions<OutboxRuntimeOptions>>(), Array.Empty<IPostConfigureOptions<OutboxRuntimeOptions>>()),
            Array.Empty<IOptionsChangeTokenSource<OutboxRuntimeOptions>>(),
            new OptionsCache<OutboxRuntimeOptions>());

        var sut = new SqlServerIdempotencyRepository(() => new SqlConnection(builder.ConnectionString), options);

        for (int i = 0; i < 5; i++)
        {
            var record = new IdempotencyRecord($"msg-{i}", $"consumer-{i}", DateTimeOffset.UtcNow);
            var inserted = await sut.TryInsertAsync(record);
            inserted.Should().BeTrue();
            await sut.PurgeExpiredRecordsAsync(DateTimeOffset.UtcNow.AddYears(-1));
        }
    }
}
