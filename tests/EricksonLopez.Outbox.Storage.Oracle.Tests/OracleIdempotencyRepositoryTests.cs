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
using EricksonLopez.Outbox.Storage.Oracle;
using Microsoft.Extensions.Options;
using NSubstitute;
using Oracle.ManagedDataAccess.Client;
using Xunit;

namespace EricksonLopez.Outbox.Storage.Oracle.Tests;

[Collection("Oracle")]
[Trait("Category", "Integration")]
public class OracleIdempotencyRepositoryTests : IAsyncLifetime
{
    private readonly OracleContainerFixture _fixture;
    private readonly IFixture _autoFixture;
    private readonly OutboxRuntimeOptions _options = new() { SchemaName = string.Empty, TableName = "messages" };

    public OracleIdempotencyRepositoryTests(OracleContainerFixture fixture)
    {
        _fixture = fixture;
        _autoFixture = new Fixture().Customize(new AutoNSubstituteCustomization());
    }

    public async Task InitializeAsync()
    {
        await OracleTestDatabase.EnsureSchemaAsync(_fixture.Container.GetConnectionString());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private OracleIdempotencyRepository CreateSut(OutboxRuntimeOptions? customOptions = null)
    {
        var optionsMonitor = Substitute.For<IOptionsMonitor<OutboxRuntimeOptions>>();
        optionsMonitor.CurrentValue.Returns(customOptions ?? _options);
        return new OracleIdempotencyRepository(() => new OracleConnection(_fixture.Container.GetConnectionString()), optionsMonitor);
    }

    [Fact]
    public void Constructor_NullConnectionFactory_ThrowsArgumentNullException()
    {
        var optionsMonitor = Substitute.For<IOptionsMonitor<OutboxRuntimeOptions>>();
        optionsMonitor.CurrentValue.Returns(_options);

        Action act = () => { _ = new OracleIdempotencyRepository(null!, optionsMonitor); };
        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionFactory");
    }

    [Fact]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        Action act = () => { _ = new OracleIdempotencyRepository(() => new OracleConnection(), null!); };
        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public async Task Operations_WithNonExistentSchema_ThrowsOracleException()
    {
        var custom = new OutboxRuntimeOptions { SchemaName = "NON_EXISTENT_SCHEMA_XYZ", TableName = "messages" };
        var sut = CreateSut(custom);
        var record = new IdempotencyRecord(Guid.NewGuid().ToString(), "c-test", DateTimeOffset.UtcNow);
        Func<Task> act = async () => await sut.TryInsertAsync(record);
        await act.Should().ThrowAsync<OracleException>();
    }

    [Fact]
    public async Task TryInsertAsync_WithoutTransaction_DisposesConnection()
    {
        OracleConnection? createdConn = null;
        var optionsMonitor = Substitute.For<IOptionsMonitor<OutboxRuntimeOptions>>();
        optionsMonitor.CurrentValue.Returns(_options);
        var sut = new OracleIdempotencyRepository(() => {
            createdConn = new OracleConnection(_fixture.Container.GetConnectionString());
            return createdConn;
        }, optionsMonitor);

        var record = new IdempotencyRecord(Guid.NewGuid().ToString(), "c-disp", DateTimeOffset.UtcNow);
        var inserted = await sut.TryInsertAsync(record);
        inserted.Should().BeTrue();
        createdConn.Should().NotBeNull();
        createdConn!.State.Should().Be(ConnectionState.Closed);
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

        var result = await sut.TryInsertAsync(record, new DbTransactionContext(tx));
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

        await sut.PurgeExpiredRecordsAsync(now.AddHours(-12));

        await using var connection = new OracleConnection(_fixture.Container.GetConnectionString());
        var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM \"MESSAGES_IDEMPOTENCY\"");
        count.Should().Be(1);
    }
}
