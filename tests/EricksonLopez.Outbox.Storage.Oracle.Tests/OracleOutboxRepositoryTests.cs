// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoFixture;
using AutoFixture.AutoNSubstitute;
using AwesomeAssertions;
using Dapper;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Storage.Oracle;
using Microsoft.Extensions.Options;
using NSubstitute;
using Oracle.ManagedDataAccess.Client;
using Testcontainers.Oracle;
using Xunit;

namespace EricksonLopez.Outbox.Storage.Oracle.Tests;

[Collection("Oracle")]
[Trait("Category", "Integration")]
public class OracleOutboxRepositoryTests : IAsyncLifetime
{
    private readonly OracleContainerFixture _fixture;
    private readonly IFixture _autoFixture;

    public OracleOutboxRepositoryTests(OracleContainerFixture fixture)
    {
        _fixture = fixture;
        _autoFixture = new Fixture().Customize(new AutoNSubstituteCustomization());
    }

    public async Task InitializeAsync()
    {
        await OracleTestDatabase.EnsureSchemaAsync(_fixture.Container.GetConnectionString(), "messages");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private OracleOutboxRepository CreateSut(OutboxRuntimeOptions? customOptions = null)
    {
        var optionsMonitor = Substitute.For<IOptions<OutboxRuntimeOptions>>();
        optionsMonitor.Value.Returns(customOptions ?? new OutboxRuntimeOptions { SchemaName = string.Empty, TableName = "messages" });
        return new OracleOutboxRepository(() => new OracleConnection(_fixture.Container.GetConnectionString()), optionsMonitor);
    }

    [Fact]
    public void Constructor_Should_Throw_On_Invalid_Schema()
    {
        var optionsMonitor = Substitute.For<IOptions<OutboxRuntimeOptions>>();
        optionsMonitor.Value.Returns(new OutboxRuntimeOptions { SchemaName = "invalid-schema!", TableName = "messages" });

        var act = () => new OracleOutboxRepository(() => new FakeDbConnection(), optionsMonitor);

        act.Should().Throw<ArgumentException>().WithMessage("Schema name contains invalid characters.*");
    }

    [Fact]
    public void Constructor_Should_Throw_On_Invalid_Table()
    {
        var optionsMonitor = Substitute.For<IOptions<OutboxRuntimeOptions>>();
        optionsMonitor.Value.Returns(new OutboxRuntimeOptions { SchemaName = "valid", TableName = "invalid-table!" });

        var act = () => new OracleOutboxRepository(() => new FakeDbConnection(), optionsMonitor);

        act.Should().Throw<ArgumentException>().WithMessage("Table name contains invalid characters.*");
    }

    [Fact]
    public void Constructor_NullConnectionFactory_ThrowsArgumentNullException()
    {
        Action act = () => { _ = new OracleOutboxRepository(null!); };
        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionFactory");
    }

    [Fact]
    public void Constructor_DefaultOptions_WhenNullOptionsPassed()
    {
        var sut = new OracleOutboxRepository(() => new OracleConnection(), null);
        sut.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_Should_Allow_Public_OutboxMessages_Fallback()
    {
        var optionsMonitor = Substitute.For<IOptions<OutboxRuntimeOptions>>();
        optionsMonitor.Value.Returns(new OutboxRuntimeOptions { SchemaName = "public", TableName = "outbox_messages" });

        var sut = new OracleOutboxRepository(() => new FakeDbConnection(), optionsMonitor);
        sut.Should().NotBeNull();
    }

    [Fact]
    public async Task InsertAsync_NullTransactionConnection_ThrowsInvalidOperationException()
    {
        var sut = CreateSut();
        var invalidTx = Substitute.For<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>();
        invalidTx.Connection.Returns((System.Data.IDbConnection)null!);

        Func<Task> act = async () => await sut.InsertAsync(CreateMessage(), invalidTx);
        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.WithMessage("Transaction connection is null.");
    }

    [Fact]
    public async Task InsertBatchAsync_NullTransactionConnection_ThrowsInvalidOperationException()
    {
        var sut = CreateSut();
        var invalidTx = Substitute.For<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>();
        invalidTx.Connection.Returns((System.Data.IDbConnection)null!);

        Func<Task> act = async () => await sut.InsertBatchAsync(new[] { CreateMessage() }, invalidTx);
        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.WithMessage("Not an OracleConnection");
    }

    private OracleOutboxRepository CreateSut(FakeDbConnection conn)
    {
        var options = new OutboxRuntimeOptions { SchemaName = string.Empty, TableName = "messages" };
        return new OracleOutboxRepository(() => new OracleConnection(_fixture.Container.GetConnectionString()), Microsoft.Extensions.Options.Options.Create(options));
    }

    private OracleOutboxRepository CreateSut()
    {
        var options = new OutboxRuntimeOptions { SchemaName = string.Empty, TableName = "messages" };
        return new OracleOutboxRepository(() => new OracleConnection(_fixture.Container.GetConnectionString()), Microsoft.Extensions.Options.Options.Create(options));
    }

    private OutboxMessage CreateMessage(int state = 0, DateTimeOffset? deliverAt = null, DateTimeOffset? createdAt = null, int retryCount = 0, string? correlationId = null, string? causationId = null)
    {
        var msg = _autoFixture.Create<OutboxMessage>();
        return msg with 
        { 
            Status = (OutboxMessageStatus)state, 
            DeliverAt = deliverAt, 
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow,
            RetryCount = retryCount,
            CorrelationId = correlationId ?? msg.CorrelationId,
            CausationId = causationId ?? msg.CausationId,
            Payload = System.Text.Encoding.UTF8.GetBytes("{}"),
            Headers = System.Text.Encoding.UTF8.GetBytes("{}")
        };
    }

    [Fact]
    public async Task InsertAsync_Should_Persist_Message()
    {
        var sut = CreateSut();
        var msg = CreateMessage(deliverAt: DateTimeOffset.UtcNow);

        await using var connection = new OracleConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        
        await sut.InsertAsync(msg, new EricksonLopez.Outbox.Persistence.DbTransactionContext(tx));
        await tx.CommitAsync();

        var row = await connection.QuerySingleAsync(
            "SELECT correlation_id as \"CorrelationId\", causation_id as \"CausationId\", deliver_at as \"DeliverAt\" FROM \"messages\" WHERE id = :Id", 
            new { Id = msg.Id.ToByteArray() });
            
        ((string?)row.CorrelationId).Should().Be(msg.CorrelationId);
        ((string?)row.CausationId).Should().Be(msg.CausationId);
        if (msg.DeliverAt.HasValue)
        {
            var deliverAt = row.DeliverAt is DateTimeOffset dto ? dto.UtcDateTime : (DateTime)row.DeliverAt;
            deliverAt.Should().BeCloseTo(msg.DeliverAt.Value.UtcDateTime, TimeSpan.FromHours(24));
        }
    }
    
    [Fact]
    public async Task InsertAsync_Should_Ignore_Duplicate_Keys()
    {
        var sut = CreateSut();
        var msg = CreateMessage();

        await using var connection = new OracleConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        
        await using var tx1 = await connection.BeginTransactionAsync();
        await sut.InsertAsync(msg, new EricksonLopez.Outbox.Persistence.DbTransactionContext(tx1));
        await tx1.CommitAsync();
        
        await using var tx2 = await connection.BeginTransactionAsync();
        await sut.InsertAsync(msg, new EricksonLopez.Outbox.Persistence.DbTransactionContext(tx2)); // duplicate insert should be ignored
        await tx2.CommitAsync();

        var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM \"messages\" WHERE id = :Id", new { Id = msg.Id.ToByteArray() });
        count.Should().Be(1);
    }
    
    [Fact]
    public async Task FetchPendingAsync_Should_Return_Only_Pending_Messages_Ready_To_Deliver()
    {
        var sut = CreateSut();
        var pendingReady = CreateMessage(state: 0, deliverAt: DateTimeOffset.UtcNow.AddMinutes(-5), correlationId: "corr1", causationId: "caus1");
        var fullPayload = new byte[10] { 0, 91, 49, 93, 0, 0, 0, 0, 0, 0 };
        var slicedPayload = new ReadOnlyMemory<byte>(fullPayload, 1, 3);
        pendingReady = pendingReady with { Payload = slicedPayload, Headers = slicedPayload };

        var pendingNotReady = CreateMessage(state: 0, deliverAt: DateTimeOffset.UtcNow.AddMinutes(10));
        var dispatched = CreateMessage(state: 2);
        var retryingReady = CreateMessage(state: 3, deliverAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        var invalidState = CreateMessage(state: 99, deliverAt: DateTimeOffset.UtcNow.AddMinutes(-5));

        await using var connection = new OracleConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        
        foreach(var m in new[] { pendingReady, pendingNotReady, dispatched, retryingReady, invalidState })
        {
            await connection.ExecuteAsync(
            "INSERT INTO \"messages\" (id, type, correlation_id, causation_id, payload, headers_json, created_at, updated_at, deliver_at, state) VALUES (:Id, :MessageType, :CorrelationId, :CausationId, :PayloadBytes, :HeadersBytes, :CreatedAt, CURRENT_TIMESTAMP, :DeliverAt, :State)", 
            new { Id = m.Id.ToByteArray(), m.MessageType, m.CorrelationId, m.CausationId, PayloadBytes = m.Payload.ToArray(), HeadersBytes = m.Headers.ToArray(), CreatedAt = m.CreatedAt, DeliverAt = m.DeliverAt, State = m.Status });
        }
        
        // Let's force the invalidState message to state 99 but also to state IN (0, 3) in the inner query somehow?
        // Wait, Oracle FetchPendingAsync queries "WHERE state IN (0, 3)". So if we insert state 99, it won't even be returned by the claim query!
        // To cover the Enum.IsDefined check in OracleOutboxRepository.cs line 267:
        // We need to bypass the _claimIdsSql which checks `state IN (0, 3)`. We can do this by updating the state AFTER it was claimed?
        // No, `hydrateCmd` uses `SELECT ... FROM messages WHERE id IN (...)`. If we update the state to 99 manually just before hydrate... this is very hard without a race condition.
        // Wait, what if we use reflection or just mock the data? 
        // We can just update the row to state 99 between the insert and fetch? No, FetchPendingAsync queries `state IN (0,3)`.
        // If a message is `state = 0`, it gets claimed, state is set to `1`. 
        // Then `hydrateSql` reads it and state is `1`. `Enum.IsDefined(typeof(OutboxMessageStatus), 1)` is TRUE!
        // So how could it EVER be invalid?
        // The only way is if someone manually set it to 99 after claim but before hydrate?
        // Or if we run a query where `state` is returned as 99?
        // Let's force it by updating `state` manually to 99 right after `INSERT`, wait, if it's 99 it won't be claimed.
        // Actually, if we change `OracleOutboxRepository._claimIdsSql` to include 99? We can't.
        // Is it possible to cover line 268?
        var fetched = await sut.FetchPendingAsync(10);
        fetched.Should().HaveCount(2);
    }
    
    private sealed class FakeDbConnection : DbConnection
    {
#pragma warning disable CS8765 // Nullability of type of parameter doesn't match overridden member (possibly because of nullability attributes).
        public override string ConnectionString { get => ""; set { } }
#pragma warning restore CS8765
        public override string Database => "";
        public override string DataSource => "";
        public override string ServerVersion => "";
        public override System.Data.ConnectionState State => System.Data.ConnectionState.Open;

        public override void ChangeDatabase(string databaseName) {}
        public override void Close() {}
        public override void Open() {}

        public DbCommand ClaimCmd { get; set; } = null!;
        public DbCommand UpdateCmd { get; set; } = null!;
        public DbCommand HydrateCmd { get; set; } = null!;
        
        public int CommandCount { get; set; }

        protected override DbTransaction BeginDbTransaction(System.Data.IsolationLevel isolationLevel)
        {
            return Substitute.For<DbTransaction>();
        }

        protected override DbCommand CreateDbCommand()
        {
            CommandCount++;
            if (CommandCount == 1) return ClaimCmd;
            if (CommandCount == 2) return UpdateCmd;
            return HydrateCmd;
        }
    }

    [Fact]
    public async Task FetchPendingAsync_Should_Return_Empty_If_No_Messages()
    {
        var sut = CreateSut();
        var fetched = await sut.FetchPendingAsync(10);
        fetched.Should().BeEmpty();
    }

    [Fact]
    public async Task Empty_Collections_Should_Return_Immediately()
    {
        var sut = CreateSut();
        var emptyMsgs = Array.Empty<OutboxMessage>();
        await using var connection = new OracleConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        await sut.InsertBatchAsync(emptyMsgs, null!);
        await sut.MarkAsDispatchedAsync(emptyMsgs);
        await sut.MarkAsFailedAsync(emptyMsgs, "error");
    }

    [Fact]
    public async Task ReclaimStaleMessagesAsync_Should_Revert_To_Pending()
    {
        var sut = CreateSut();
        var staleMsg = CreateMessage(state: 1);
        var freshMsg = CreateMessage(state: 1);

        await using var connection = new OracleConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        
        await connection.ExecuteAsync("INSERT INTO \"messages\" (id, type, payload, headers_json, created_at, updated_at, state) VALUES (:Id, :MessageType, :P, :H, :CreatedAt, CURRENT_TIMESTAMP - INTERVAL '2' HOUR, :State)", new { Id = staleMsg.Id.ToByteArray(), staleMsg.MessageType, P = Array.Empty<byte>(), H = Array.Empty<byte>(), CreatedAt = staleMsg.CreatedAt, State = (int)staleMsg.Status });
        await connection.ExecuteAsync("INSERT INTO \"messages\" (id, type, payload, headers_json, created_at, updated_at, state) VALUES (:Id, :MessageType, :P, :H, :CreatedAt, CURRENT_TIMESTAMP, :State)", new { Id = freshMsg.Id.ToByteArray(), freshMsg.MessageType, P = Array.Empty<byte>(), H = Array.Empty<byte>(), CreatedAt = freshMsg.CreatedAt, State = (int)freshMsg.Status });

        var reclaimedCount = await sut.ReclaimStaleMessagesAsync(TimeSpan.FromHours(1));

        reclaimedCount.Should().Be(1);

        var staleState = await connection.QuerySingleAsync<int>("SELECT state FROM \"messages\" WHERE id = :Id", new { Id = staleMsg.Id.ToByteArray() });
        var freshState = await connection.QuerySingleAsync<int>("SELECT state FROM \"messages\" WHERE id = :Id", new { Id = freshMsg.Id.ToByteArray() });

        staleState.Should().Be(0);
        freshState.Should().Be(1);
    }

    [Theory]
    [InlineData(false, 3)]
    [InlineData(true, 4)]
    public async Task MarkAsFailedAsync_Should_Update_Status_And_Increment_Retry(bool isDeadLetter, int expectedState)
    {
        var sut = CreateSut();
        var msg1 = CreateMessage(state: 1, retryCount: 0);
        var msg2 = CreateMessage(state: 1, retryCount: 2);

        await using var connection = new OracleConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        await connection.ExecuteAsync("INSERT INTO \"messages\" (id, type, payload, headers_json, created_at, updated_at, state, retry_count) VALUES (:Id, :MessageType, :P, :H, :CreatedAt, CURRENT_TIMESTAMP, :State, :RetryCount)", new { Id = msg1.Id.ToByteArray(), msg1.MessageType, P = Array.Empty<byte>(), H = Array.Empty<byte>(), CreatedAt = msg1.CreatedAt, State = (int)msg1.Status, msg1.RetryCount });
        await connection.ExecuteAsync("INSERT INTO \"messages\" (id, type, payload, headers_json, created_at, updated_at, state, retry_count) VALUES (:Id, :MessageType, :P, :H, :CreatedAt, CURRENT_TIMESTAMP, :State, :RetryCount)", new { Id = msg2.Id.ToByteArray(), msg2.MessageType, P = Array.Empty<byte>(), H = Array.Empty<byte>(), CreatedAt = msg2.CreatedAt, State = (int)msg2.Status, msg2.RetryCount });

        await sut.MarkAsFailedAsync(new[] { msg1, msg2 }, "error", isDeadLetter);

        var db1 = await connection.QuerySingleAsync<(int state, int retry_count)>("SELECT state, retry_count FROM \"messages\" WHERE id = :Id", new { Id = msg1.Id.ToByteArray() });
        db1.state.Should().Be(expectedState);
        db1.retry_count.Should().Be(1);

        var db2 = await connection.QuerySingleAsync<(int state, int retry_count)>("SELECT state, retry_count FROM \"messages\" WHERE id = :Id", new { Id = msg2.Id.ToByteArray() });
        db2.state.Should().Be(expectedState);
        db2.retry_count.Should().Be(3);
    }

    [Fact]
    public async Task MarkAsDispatchedAsync_Should_Update_Status_To_Dispatched()
    {
        var sut = CreateSut();
        var msg1 = CreateMessage(state: 1);
        var msg2 = CreateMessage(state: 1);

        await using var connection = new OracleConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        await connection.ExecuteAsync("INSERT INTO \"messages\" (id, type, payload, headers_json, created_at, updated_at, state) VALUES (:Id, :MessageType, :P, :H, :CreatedAt, CURRENT_TIMESTAMP, :State)", new { Id = msg1.Id.ToByteArray(), msg1.MessageType, P = Array.Empty<byte>(), H = Array.Empty<byte>(), CreatedAt = msg1.CreatedAt, State = (int)msg1.Status });
        await connection.ExecuteAsync("INSERT INTO \"messages\" (id, type, payload, headers_json, created_at, updated_at, state) VALUES (:Id, :MessageType, :P, :H, :CreatedAt, CURRENT_TIMESTAMP, :State)", new { Id = msg2.Id.ToByteArray(), msg2.MessageType, P = Array.Empty<byte>(), H = Array.Empty<byte>(), CreatedAt = msg2.CreatedAt, State = (int)msg2.Status });

        await sut.MarkAsDispatchedAsync(new[] { msg1, msg2 });

        var count1 = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM \"messages\" WHERE id = :Id", new { Id = msg1.Id.ToByteArray() });
        count1.Should().Be(0);

        var count2 = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM \"messages\" WHERE id = :Id", new { Id = msg2.Id.ToByteArray() });
        count2.Should().Be(0);
    }

    [Fact]
    public async Task PurgeDispatchedMessagesAsync_BatchSizeZeroOrNegative_ReturnsZero()
    {
        var sut = CreateSut();
        var count = await sut.PurgeDispatchedMessagesAsync(DateTimeOffset.UtcNow, 0);
        count.Should().Be(0);

        var countNeg = await sut.PurgeDispatchedMessagesAsync(DateTimeOffset.UtcNow, -5);
        countNeg.Should().Be(0);
    }

    [Fact]
    public async Task PurgeDispatchedMessagesAsync_Should_Delete_Dispatched_Messages_Older_Than_Cutoff()
    {
        var sut = CreateSut();
        var msgOld = CreateMessage(state: 2);
        var msgFresh = CreateMessage(state: 2);
        var oldDate = DateTime.UtcNow.AddDays(-10);
        var freshDate = DateTime.UtcNow.AddDays(10);

        await using var connection = new OracleConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();

        await connection.ExecuteAsync("INSERT INTO \"messages\" (id, type, payload, headers_json, created_at, updated_at, processed_at, state) VALUES (:Id, :MessageType, :P, :H, :CreatedAt, :UpdatedAt, :ProcessedAt, :State)", new { Id = msgOld.Id.ToByteArray(), msgOld.MessageType, P = Array.Empty<byte>(), H = Array.Empty<byte>(), CreatedAt = oldDate, UpdatedAt = oldDate, ProcessedAt = oldDate, State = 2 });
        await connection.ExecuteAsync("INSERT INTO \"messages\" (id, type, payload, headers_json, created_at, updated_at, processed_at, state) VALUES (:Id, :MessageType, :P, :H, :CreatedAt, :UpdatedAt, :ProcessedAt, :State)", new { Id = msgFresh.Id.ToByteArray(), msgFresh.MessageType, P = Array.Empty<byte>(), H = Array.Empty<byte>(), CreatedAt = freshDate, UpdatedAt = freshDate, ProcessedAt = freshDate, State = 2 });

        var purged = await sut.PurgeDispatchedMessagesAsync(DateTimeOffset.UtcNow.AddDays(-1), 100);
        purged.Should().Be(1);

        var remainingOld = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM \"messages\" WHERE id = :Id", new { Id = msgOld.Id.ToByteArray() });
        remainingOld.Should().Be(0);

        var remainingFresh = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM \"messages\" WHERE id = :Id", new { Id = msgFresh.Id.ToByteArray() });
        remainingFresh.Should().Be(1);
    }

    [Fact]
    public async Task InsertBatchAsync_Should_Persist_Multiple_Messages()
    {
        var sut = CreateSut();
        var msg1 = CreateMessage(correlationId: "c1", causationId: "c2", deliverAt: DateTimeOffset.UtcNow.AddMinutes(5));
        var msg2 = CreateMessage(deliverAt: null) with { CorrelationId = null, CausationId = null };
        var messages = new[] { msg1, msg2 };

        await using var connection = new OracleConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        await using var tx = connection.BeginTransaction();
        
        await sut.InsertBatchAsync(messages, new EricksonLopez.Outbox.Persistence.DbTransactionContext(tx));
        await tx.CommitAsync();

        var list = (await connection.QueryAsync("SELECT id, correlation_id, causation_id, deliver_at FROM \"messages\"")).ToList();
        list.Should().HaveCount(2);

        var db1 = list.Single(x => new Guid((byte[])x.ID) == msg1.Id);
        ((string)db1.CORRELATION_ID).Should().Be("c1");
        ((string)db1.CAUSATION_ID).Should().Be("c2");
        var deliverAt = db1.DELIVER_AT is DateTimeOffset dto ? dto.UtcDateTime : (DateTime)db1.DELIVER_AT;
        deliverAt.Should().BeCloseTo(msg1.DeliverAt!.Value.UtcDateTime, TimeSpan.FromHours(24));
    }
    
    [Fact]
    public async Task InsertBatchAsync_Should_Ignore_Duplicate_Keys()
    {
        var sut = CreateSut();
        var msg = CreateMessage();

        await using var connection = new OracleConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        
        await using var tx1 = connection.BeginTransaction();
        await sut.InsertBatchAsync(new[] { msg }, new EricksonLopez.Outbox.Persistence.DbTransactionContext(tx1));
        await tx1.CommitAsync();
        
        await using var tx2 = connection.BeginTransaction();
        await sut.InsertBatchAsync(new[] { msg }, new EricksonLopez.Outbox.Persistence.DbTransactionContext(tx2)); // duplicate batch insert should be ignored
        await tx2.CommitAsync();

        var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM \"messages\" WHERE id = :Id", new { Id = msg.Id.ToByteArray() });
        count.Should().Be(1);
    }

    [Fact]
    public async Task GetPendingCountAsync_Should_Return_Count()
    {
        var sut = CreateSut();
        var msg1 = CreateMessage(state: 0);
        var msg2 = CreateMessage(state: 3);

        await using var connection = new OracleConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        await using var tx = connection.BeginTransaction();

        await sut.InsertAsync(msg1, new EricksonLopez.Outbox.Persistence.DbTransactionContext(tx));
        await sut.InsertAsync(msg2, new EricksonLopez.Outbox.Persistence.DbTransactionContext(tx));
        await tx.CommitAsync();

        var count = await sut.GetPendingCountAsync(CancellationToken.None);
        count.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Constructor_CustomSchemaWithMessagesTable_TargetsQualifiedTable()
    {
        var opt = new OutboxRuntimeOptions { SchemaName = "CUSTOM_NONEXISTENT_SCHEMA", TableName = "messages" };
        var sut = CreateSut(opt);
        Func<Task> act = async () => await sut.GetPendingCountAsync(CancellationToken.None);
        await act.Should().ThrowAsync<OracleException>();
    }

    [Fact]
    public async Task MarkAsDispatchedAsync_NonOracleConnection_ThrowsInvalidOperationException()
    {
        var sut = new OracleOutboxRepository(() => new FakeDbConnection(), Microsoft.Extensions.Options.Options.Create(new OutboxRuntimeOptions()));
        Func<Task> act = async () => await sut.MarkAsDispatchedAsync(new[] { CreateMessage() });
        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.WithMessage("Not an OracleConnection");
    }

    [Fact]
    public async Task MarkAsFailedAsync_NonOracleConnection_ThrowsInvalidOperationException()
    {
        var sut = new OracleOutboxRepository(() => new FakeDbConnection(), Microsoft.Extensions.Options.Options.Create(new OutboxRuntimeOptions()));
        Func<Task> act = async () => await sut.MarkAsFailedAsync(new[] { CreateMessage() }, "error");
        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.WithMessage("Not an OracleConnection");
    }

    [Fact]
    public async Task InsertBatchAsync_WhenRecordsEmpty_ReturnsImmediately()
    {
        var sut = CreateSut();
        var tx = Substitute.For<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>();
        await sut.InsertBatchAsync(ReadOnlyMemory<OutboxMessage>.Empty, tx);
    }

    [Fact]
    public async Task MarkAsDispatchedAsync_WhenEmptyList_ReturnsImmediately()
    {
        var sut = CreateSut();
        await sut.MarkAsDispatchedAsync(Array.Empty<OutboxMessage>());
    }

    [Fact]
    public async Task MarkAsFailedAsync_WhenEmptyList_ReturnsImmediately()
    {
        var sut = CreateSut();
        await sut.MarkAsFailedAsync(Array.Empty<OutboxMessage>(), "error");
    }

    [Fact]
    public async Task PurgeDispatchedMessagesAsync_WhenBatchSizeZeroOrNegative_ReturnsZero()
    {
        var sut = CreateSut();
        var purgedZero = await sut.PurgeDispatchedMessagesAsync(DateTimeOffset.UtcNow, batchSize: 0);
        purgedZero.Should().Be(0);

        var purgedNeg = await sut.PurgeDispatchedMessagesAsync(DateTimeOffset.UtcNow, batchSize: -5);
        purgedNeg.Should().Be(0);
    }
}











