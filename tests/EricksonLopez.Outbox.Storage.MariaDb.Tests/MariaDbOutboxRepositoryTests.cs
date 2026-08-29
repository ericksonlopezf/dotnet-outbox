// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
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
public class MariaDbOutboxRepositoryTests : IAsyncLifetime
{
    private readonly MariaDbContainerFixture _fixture;
    private readonly IFixture _autoFixture;
    private readonly OutboxRuntimeOptions _options = new() { SchemaName = "testdb", TableName = "outbox_messages" };
    public string InstanceId => _options.InstanceId;

    public MariaDbOutboxRepositoryTests(MariaDbContainerFixture fixture)
    {
        _fixture = fixture;
        _autoFixture = new Fixture().Customize(new AutoNSubstituteCustomization());
    }

    public async Task InitializeAsync()
    {
        await MariaDbTestDatabase.EnsureSchemaAsync(_fixture.Container.GetConnectionString(), _options.TableName);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private MariaDbOutboxRepository CreateSut(OutboxRuntimeOptions? customOptions = null)
    {
        return new MariaDbOutboxRepository(
            () => new MySqlConnection(_fixture.Container.GetConnectionString()),
            Options.Create(customOptions ?? _options));
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
    public void Constructor_NullConnectionFactory_ThrowsArgumentNullException()
    {
        Action act = () => { _ = new MariaDbOutboxRepository(null!); };
        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionFactory");
    }

    [Theory]
    [InlineData("bad schema!")]
    [InlineData("schema-with-dash")]
    [InlineData("schema.with.dot")]
    public void Constructor_InvalidSchemaName_ThrowsArgumentException(string schema)
    {
        var opt = new OutboxRuntimeOptions { SchemaName = schema, TableName = "outbox_messages" };
        Action act = () => { _ = new MariaDbOutboxRepository(() => new MySqlConnection(), Options.Create(opt)); };
        act.Should().Throw<ArgumentException>().WithParameterName("options");
    }

    [Theory]
    [InlineData("bad table!")]
    [InlineData("table-with-dash")]
    [InlineData("table;inject")]
    public void Constructor_InvalidTableName_ThrowsArgumentException(string table)
    {
        var opt = new OutboxRuntimeOptions { SchemaName = "testdb", TableName = table };
        Action act = () => { _ = new MariaDbOutboxRepository(() => new MySqlConnection(), Options.Create(opt)); };
        act.Should().Throw<ArgumentException>().WithParameterName("options");
    }

    [Fact]
    public void Constructor_DefaultOptions_WhenNullOptionsPassed()
    {
        var sut = new MariaDbOutboxRepository(() => new MySqlConnection(), null);
        sut.Should().NotBeNull();
    }

    [Fact]
    public async Task Operations_WithPublicSchemaAndOutboxMessagesTable_RewritesNameToDefaultAndSucceeds()
    {
        var opt = new OutboxRuntimeOptions { SchemaName = "public", TableName = "outbox_messages" };
        var sut = CreateSut(opt);
        var msg = CreateMessage();

        await using var connection = new MySqlConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();

        await sut.InsertAsync(msg, new DbTransactionContext(tx));
        await tx.CommitAsync();

        var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM outbox_messages WHERE id = @Id", new { Id = msg.Id.ToString() });
        count.Should().Be(1);
    }

    [Fact]
    public async Task InsertAsync_NullTransactionConnection_ThrowsInvalidOperationException()
    {
        var sut = CreateSut();
        var invalidTx = Substitute.For<IOutboxTransactionContext>();
        invalidTx.Connection.Returns((IDbConnection)null!);

        Func<Task> act = async () => await sut.InsertAsync(CreateMessage(), invalidTx);
        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.WithMessage("Transaction connection is null.");
    }

    [Fact]
    public async Task InsertAsync_Should_Persist_Message()
    {
        var sut = CreateSut();
        var msg = CreateMessage(deliverAt: DateTimeOffset.UtcNow);

        await using var connection = new MySqlConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();

        await sut.InsertAsync(msg, new DbTransactionContext(tx));
        await tx.CommitAsync();

        var row = await connection.QuerySingleAsync(
            "SELECT correlation_id as CorrelationId, causation_id as CausationId, deliver_at as DeliverAt FROM outbox_messages WHERE id = @Id",
            new { Id = msg.Id.ToString() });

        ((string?)row.CorrelationId).Should().Be(msg.CorrelationId);
        ((string?)row.CausationId).Should().Be(msg.CausationId);
        if (msg.DeliverAt.HasValue)
        {
            ((DateTime?)row.DeliverAt).Should().BeCloseTo(msg.DeliverAt.Value.UtcDateTime, TimeSpan.FromMilliseconds(1));
        }
    }

    [Fact]
    public async Task InsertAsync_WithNullCorrelationCausationAndDeliverAt_Should_Persist_Nulls()
    {
        var sut = CreateSut();
        var msg = CreateMessage(deliverAt: null) with { CorrelationId = null, CausationId = null };

        await using var connection = new MySqlConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();

        await sut.InsertAsync(msg, new DbTransactionContext(tx));
        await tx.CommitAsync();

        var row = await connection.QuerySingleAsync(
            "SELECT correlation_id as CorrelationId, causation_id as CausationId, deliver_at as DeliverAt FROM outbox_messages WHERE id = @Id",
            new { Id = msg.Id.ToString() });

        ((string?)row.CorrelationId).Should().BeNull();
        ((string?)row.CausationId).Should().BeNull();
        ((DateTime?)row.DeliverAt).Should().BeNull();
    }

    [Fact]
    public async Task InsertBatchAsync_InvalidTransactionConnection_ThrowsInvalidOperationException()
    {
        var sut = CreateSut();
        var invalidTx = Substitute.For<IOutboxTransactionContext>();
        invalidTx.Connection.Returns(Substitute.For<IDbConnection>());

        Func<Task> act = async () => await sut.InsertBatchAsync(new[] { CreateMessage() }, invalidTx);
        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.WithMessage("Transaction connection is null.");
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

        await using var connection = new MySqlConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();

        foreach (var m in new[] { pendingReady, pendingNotReady, dispatched, retryingReady })
        {
            await connection.ExecuteAsync(
            "INSERT INTO outbox_messages (id, type, correlation_id, causation_id, payload, headers_json, created_at, updated_at, deliver_at, state, owner_id) VALUES (@Id, @MessageType, @CorrelationId, @CausationId, @PayloadBytes, @HeadersBytes, @CreatedAt, UTC_TIMESTAMP(6), @DeliverAt, @State, @OwnerId)",
            new { Id = m.Id.ToString(), m.MessageType, m.CorrelationId, m.CausationId, PayloadBytes = m.Payload.ToArray(), HeadersBytes = m.Headers.ToArray(), CreatedAt = m.CreatedAt.UtcDateTime, DeliverAt = m.DeliverAt?.UtcDateTime, State = m.Status, OwnerId = InstanceId });
        }

        var fetched = await sut.FetchPendingAsync(10);
        fetched.Should().HaveCount(2);

        var stateReady = await connection.QuerySingleAsync<(int state, string owner)>(
            "SELECT state, owner_id FROM outbox_messages WHERE id = @Id", new { Id = pendingReady.Id.ToString() });
        stateReady.state.Should().Be(1);
        stateReady.owner.Should().Be(InstanceId);
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

        await using var connection = new MySqlConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();

        // Insert stale
        await connection.ExecuteAsync("INSERT INTO outbox_messages (id, type, payload, headers_json, created_at, updated_at, state, owner_id) VALUES (@Id, @MessageType, '{}', '{}', @CreatedAt, DATE_SUB(UTC_TIMESTAMP(6), INTERVAL 7200 SECOND), @State, @OwnerId)", new { Id = staleMsg.Id.ToString(), staleMsg.MessageType, staleMsg.CreatedAt, State = staleMsg.Status, OwnerId = InstanceId });
        // Insert fresh
        await connection.ExecuteAsync("INSERT INTO outbox_messages (id, type, payload, headers_json, created_at, updated_at, state, owner_id) VALUES (@Id, @MessageType, '{}', '{}', @CreatedAt, UTC_TIMESTAMP(6), @State, @OwnerId)", new { Id = freshMsg.Id.ToString(), freshMsg.MessageType, freshMsg.CreatedAt, State = freshMsg.Status, OwnerId = InstanceId });

        var reclaimedCount = await sut.ReclaimStaleMessagesAsync(TimeSpan.FromHours(1));

        reclaimedCount.Should().Be(1);

        var staleState = await connection.QuerySingleAsync<int>("SELECT state FROM outbox_messages WHERE id = @Id", new { Id = staleMsg.Id.ToString() });
        var freshState = await connection.QuerySingleAsync<int>("SELECT state FROM outbox_messages WHERE id = @Id", new { Id = freshMsg.Id.ToString() });

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

        await using var connection = new MySqlConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        await connection.ExecuteAsync("INSERT INTO outbox_messages (id, type, payload, headers_json, created_at, updated_at, state, retry_count, owner_id) VALUES (@Id, @MessageType, '{}', '{}', @CreatedAt, UTC_TIMESTAMP(6), @State, @RetryCount, @OwnerId)", new { Id = msg1.Id.ToString(), msg1.MessageType, msg1.CreatedAt, State = msg1.Status, msg1.RetryCount, OwnerId = InstanceId });
        await connection.ExecuteAsync("INSERT INTO outbox_messages (id, type, payload, headers_json, created_at, updated_at, state, retry_count, owner_id) VALUES (@Id, @MessageType, '{}', '{}', @CreatedAt, UTC_TIMESTAMP(6), @State, @RetryCount, @OwnerId)", new { Id = msg2.Id.ToString(), msg2.MessageType, msg2.CreatedAt, State = msg2.Status, msg2.RetryCount, OwnerId = InstanceId });

        await sut.MarkAsFailedAsync(new[] { msg1, msg2 }, "error", isDeadLetter);

        var db1 = await connection.QuerySingleAsync<(int state, int retry_count, string error)>("SELECT state, retry_count, error FROM outbox_messages WHERE id = @Id", new { Id = msg1.Id.ToString() });
        db1.state.Should().Be(expectedState);
        db1.retry_count.Should().Be(1);
        db1.error.Should().Be("error");

        var db2 = await connection.QuerySingleAsync<(int state, int retry_count, string error)>("SELECT state, retry_count, error FROM outbox_messages WHERE id = @Id", new { Id = msg2.Id.ToString() });
        db2.state.Should().Be(expectedState);
        db2.retry_count.Should().Be(3);
    }

    [Fact]
    public async Task MarkAsDispatchedAsync_Should_Update_Status_To_Dispatched()
    {
        var sut = CreateSut();
        var msg1 = CreateMessage(state: 1);
        var msg2 = CreateMessage(state: 1);

        await using var connection = new MySqlConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        await connection.ExecuteAsync("INSERT INTO outbox_messages (id, type, payload, headers_json, created_at, updated_at, state, owner_id) VALUES (@Id, @MessageType, '{}', '{}', @CreatedAt, UTC_TIMESTAMP(6), @State, @OwnerId)", new { Id = msg1.Id.ToString(), msg1.MessageType, msg1.CreatedAt, State = msg1.Status, OwnerId = InstanceId });
        await connection.ExecuteAsync("INSERT INTO outbox_messages (id, type, payload, headers_json, created_at, updated_at, state, owner_id) VALUES (@Id, @MessageType, '{}', '{}', @CreatedAt, UTC_TIMESTAMP(6), @State, @OwnerId)", new { Id = msg2.Id.ToString(), msg2.MessageType, msg2.CreatedAt, State = msg2.Status, OwnerId = InstanceId });

        await sut.MarkAsDispatchedAsync(new[] { msg1, msg2 });

        var count1 = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM outbox_messages WHERE id IN (@Id1, @Id2)", new { Id1 = msg1.Id.ToString(), Id2 = msg2.Id.ToString() });
        count1.Should().Be(0);
    }

    [Fact]
    public async Task InsertBatchAsync_Should_Persist_Multiple_Messages()
    {
        var sut = CreateSut();
        var deliverTime = DateTimeOffset.UtcNow.AddMinutes(5);
        var msg1 = CreateMessage(correlationId: "c1", causationId: "c2", deliverAt: deliverTime);
        var msg2 = CreateMessage(deliverAt: null) with { CorrelationId = null, CausationId = null };
        var messages = new[] { msg1, msg2 };

        await using var connection = new MySqlConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        await using var tx = connection.BeginTransaction();

        await sut.InsertBatchAsync(messages, new DbTransactionContext(tx));
        await tx.CommitAsync();

        var row1 = await connection.QuerySingleAsync("SELECT id, correlation_id, causation_id, deliver_at FROM outbox_messages WHERE id = @Id", new { Id = msg1.Id.ToString() });
        ((string)row1.correlation_id).Should().Be("c1");
        ((string)row1.causation_id).Should().Be("c2");
        ((DateTime?)row1.deliver_at).Should().NotBeNull();

        var row2 = await connection.QuerySingleAsync("SELECT id, correlation_id, causation_id, deliver_at FROM outbox_messages WHERE id = @Id", new { Id = msg2.Id.ToString() });
        ((string?)row2.correlation_id).Should().BeNull();
        ((string?)row2.causation_id).Should().BeNull();
        ((DateTime?)row2.deliver_at).Should().BeNull();
    }

    [Fact]
    public async Task GetPendingCountAsync_Should_Return_Count()
    {
        var sut = CreateSut();
        var msg1 = CreateMessage(state: 0);
        var msg2 = CreateMessage(state: 3);

        await using var connection = new MySqlConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();

        await sut.InsertAsync(msg1, new DbTransactionContext(tx));
        await sut.InsertAsync(msg2, new DbTransactionContext(tx));
        await tx.CommitAsync();

        var count = await sut.GetPendingCountAsync(CancellationToken.None);
        count.Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public async Task Constructor_PublicSchemaWithCustomTable_TargetsQualifiedTable()
    {
        var opt = new OutboxRuntimeOptions { SchemaName = "public", TableName = "custom_nonexistent_table" };
        var sut = CreateSut(opt);
        Func<Task> act = async () => await sut.GetPendingCountAsync(CancellationToken.None);
        await act.Should().ThrowAsync<MySqlException>();
    }

    [Fact]
    public async Task Constructor_CustomSchemaWithOutboxMessagesTable_TargetsQualifiedTable()
    {
        var opt = new OutboxRuntimeOptions { SchemaName = "custom_nonexistent_schema", TableName = "outbox_messages" };
        var sut = CreateSut(opt);
        Func<Task> act = async () => await sut.GetPendingCountAsync(CancellationToken.None);
        var ex = await act.Should().ThrowAsync<MySqlException>();
        ex.Which.Message.Should().Contain("custom_nonexistent_schema");
    }

    [Fact]
    public async Task InsertBatchAsync_Should_Check_Cancellation_Token()
    {
        var sut = CreateSut();
        var messages = new[] { CreateMessage() };
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await using var connection = new MySqlConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        await using var tx = connection.BeginTransaction();

        Func<Task> act = async () => await sut.InsertBatchAsync(messages, new DbTransactionContext(tx), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task FetchPendingAsync_Should_Check_Cancellation_Token()
    {
        var sut = CreateSut();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = async () => await sut.FetchPendingAsync(10, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task FetchPendingAsync_Should_Map_Null_Fields_Correctly()
    {
        var sut = CreateSut();
        var msg = CreateMessage(state: 0, deliverAt: null, correlationId: null, causationId: null);

        await using var connection = new MySqlConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "INSERT INTO outbox_messages (id, type, correlation_id, causation_id, payload, headers_json, created_at, updated_at, processed_at, deliver_at, state, owner_id) VALUES (@Id, @MessageType, NULL, NULL, @PayloadBytes, @HeadersBytes, @CreatedAt, UTC_TIMESTAMP(6), NULL, NULL, @State, NULL)",
            new { Id = msg.Id.ToString(), msg.MessageType, PayloadBytes = msg.Payload.ToArray(), HeadersBytes = msg.Headers.ToArray(), CreatedAt = msg.CreatedAt.UtcDateTime, State = msg.Status });

        var fetched = await sut.FetchPendingAsync(10);

        fetched.Should().HaveCount(1);
        var f = fetched[0];
        f.CorrelationId.Should().BeNull();
        f.CausationId.Should().BeNull();
        f.DeliverAt.Should().BeNull();
        f.ProcessedAt.Should().BeNull();
        f.Error.Should().BeNull();
    }

    [Fact]
    public async Task FetchPendingAsync_Should_Map_NonNull_Fields_Correctly()
    {
        var sut = CreateSut();
        var pBytes = System.Text.Encoding.UTF8.GetBytes("{\"mariadb_custom\":\"payload_value\"}");
        var hBytes = System.Text.Encoding.UTF8.GetBytes("{\"mariadb_custom\":\"header_value\"}");
        var msg = CreateMessage(state: 0, deliverAt: DateTimeOffset.UtcNow.AddMinutes(-10), correlationId: "c1", causationId: "c2") with
        {
            Payload = pBytes,
            Headers = hBytes
        };

        await using var connection = new MySqlConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "INSERT INTO outbox_messages (id, type, correlation_id, causation_id, payload, headers_json, created_at, updated_at, processed_at, deliver_at, state, owner_id, error) VALUES (@Id, @MessageType, @CorrelationId, @CausationId, @PayloadBytes, @HeadersBytes, @CreatedAt, UTC_TIMESTAMP(6), @ProcessedAt, @DeliverAt, @State, @OwnerId, 'some err')",
            new { Id = msg.Id.ToString(), msg.MessageType, msg.CorrelationId, msg.CausationId, PayloadBytes = pBytes, HeadersBytes = hBytes, CreatedAt = msg.CreatedAt.UtcDateTime, ProcessedAt = DateTime.UtcNow, DeliverAt = msg.DeliverAt!.Value.UtcDateTime, State = msg.Status, OwnerId = "owner1" });

        var fetched = await sut.FetchPendingAsync(10);

        fetched.Should().HaveCount(1);
        var f = fetched[0];
        f.CorrelationId.Should().Be("c1");
        f.CausationId.Should().Be("c2");
        f.DeliverAt.Should().NotBeNull();
        f.ProcessedAt.Should().NotBeNull();
        f.Error.Should().Be("some err");
        System.Text.Encoding.UTF8.GetString(f.Payload.Span).Should().Be("{\"mariadb_custom\":\"payload_value\"}");
        System.Text.Encoding.UTF8.GetString(f.Headers.Span).Should().Be("{\"mariadb_custom\":\"header_value\"}");
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
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var oldDate = DateTime.UtcNow.AddDays(-10);
        var freshDate = DateTime.UtcNow.AddDays(10);

        await using var connection = new MySqlConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();

        // Old dispatched
        await connection.ExecuteAsync(
            "INSERT INTO outbox_messages (id, type, payload, headers_json, created_at, updated_at, processed_at, state) VALUES (@Id, 'type', '{}', '{}', @OldDate, @OldDate, @OldDate, 2)",
            new { Id = id1.ToString(), OldDate = oldDate });

        // Fresh dispatched
        await connection.ExecuteAsync(
            "INSERT INTO outbox_messages (id, type, payload, headers_json, created_at, updated_at, processed_at, state) VALUES (@Id, 'type', '{}', '{}', @FreshDate, @FreshDate, @FreshDate, 2)",
            new { Id = id2.ToString(), FreshDate = freshDate });

        var purged = await sut.PurgeDispatchedMessagesAsync(DateTimeOffset.UtcNow.AddDays(-1), 100);
        purged.Should().Be(1);

        var remaining = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM outbox_messages WHERE id = @Id", new { Id = id1.ToString() });
        remaining.Should().Be(0);

        var kept = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM outbox_messages WHERE id = @Id", new { Id = id2.ToString() });
        kept.Should().Be(1);
    }
}
