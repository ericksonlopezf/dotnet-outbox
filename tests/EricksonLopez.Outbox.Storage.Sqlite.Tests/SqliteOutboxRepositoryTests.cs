// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Dapper;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Storage.Sqlite;
using EricksonLopez.Outbox.Tests.Infrastructure;
using Microsoft.Data.Sqlite;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Storage.Sqlite.Tests;

public class SqliteOutboxRepositoryTests : IDisposable
{
    private readonly string _connectionString;
    private readonly SqliteConnection _connection;

    public SqliteOutboxRepositoryTests()
    {
        _connectionString = $"Data Source=outbox_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        
        // In-memory sqlite shared cache per test instance so connections to _connectionString share DB.
        _connection = new SqliteConnection(_connectionString);
        _connection.Open();

        SqliteTestDatabase.EnsureSchema(_connection);
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private SqliteOutboxRepository CreateSut()
    {
        return new SqliteOutboxRepository(() => new SqliteConnection(_connectionString));
    }

    private static OutboxMessage CreateMessage(
        int state = 0, 
        DateTimeOffset? deliverAt = null, 
        DateTimeOffset? createdAt = null, 
        int retryCount = 0, 
        string? correlationId = null, 
        string? causationId = null)
    {
        var builder = new OutboxMessageTestDataBuilder()
            .WithStatus((OutboxMessageStatus)state)
            .WithCreatedAt(createdAt ?? DateTimeOffset.UtcNow)
            .WithRetryCount(retryCount)
            .WithPayload(System.Text.Encoding.UTF8.GetBytes("{}"))
            .WithHeaders(System.Text.Encoding.UTF8.GetBytes("{}"));

        if (deliverAt.HasValue) builder.WithDeliverAt(deliverAt.Value);
        if (correlationId != null) builder.WithCorrelationId(correlationId);
        if (causationId != null) builder.WithCausationId(causationId);

        return builder.Build();
    }

    [Fact]
    public void Constructor_NullConnectionFactory_ThrowsArgumentNullException()
    {
        Action act = () => { _ = new SqliteOutboxRepository(null!); };
        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionFactory");
    }

    [Theory]
    [InlineData("bad table!")]
    [InlineData("table-with-dash")]
    [InlineData("table;inject")]
    public void Constructor_InvalidTableName_ThrowsArgumentException(string table)
    {
        var opt = new OutboxRuntimeOptions { TableName = table };
        Action act = () => { _ = new SqliteOutboxRepository(() => new SqliteConnection(), Microsoft.Extensions.Options.Options.Create(opt)); };
        act.Should().Throw<ArgumentException>().WithParameterName("options");
    }

    [Fact]
    public void Constructor_DefaultOptions_WhenNullOptionsPassed()
    {
        var sut = new SqliteOutboxRepository(() => new SqliteConnection(), null);
        sut.Should().NotBeNull();
    }

    [Fact]
    public async Task InsertAsync_NullTransactionConnection_ThrowsInvalidOperationException()
    {
        var sut = CreateSut();
        var invalidTx = NSubstitute.Substitute.For<IOutboxTransactionContext>();
        invalidTx.Connection.Returns((IDbConnection)null!);

        Func<Task> act = async () => await sut.InsertAsync(CreateMessage(), invalidTx);
        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.WithMessage("Transaction connection is null.");
    }

    [Fact]
    public async Task InsertBatchAsync_NullTransactionConnection_ThrowsInvalidOperationException()
    {
        var sut = CreateSut();
        var invalidTx = NSubstitute.Substitute.For<IOutboxTransactionContext>();
        invalidTx.Connection.Returns((IDbConnection)null!);

        Func<Task> act = async () => await sut.InsertBatchAsync(new[] { CreateMessage() }, invalidTx);
        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.WithMessage("Transaction connection is null.");
    }

    [Fact]
    public async Task InsertAsync_WhenValidMessage_PersistsToDatabase()
    {
        var sut = CreateSut();
        var msg = CreateMessage(deliverAt: DateTimeOffset.UtcNow);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        
        await sut.InsertAsync(msg, new EricksonLopez.Outbox.Persistence.DbTransactionContext(tx));
        await tx.CommitAsync();

        var row = await connection.QuerySingleAsync(
            "SELECT correlation_id as CorrelationId, causation_id as CausationId, deliver_at as DeliverAt FROM messages WHERE id = @Id", 
            new { Id = msg.Id.ToString() });
            
        ((string?)row.CorrelationId).Should().Be(msg.CorrelationId);
        ((string?)row.CausationId).Should().Be(msg.CausationId);
        if (msg.DeliverAt.HasValue)
        {
            var parsedDate = DateTimeOffset.Parse((string)row.DeliverAt, null, System.Globalization.DateTimeStyles.RoundtripKind);
            parsedDate.Should().BeCloseTo(msg.DeliverAt.Value.UtcDateTime, TimeSpan.FromMilliseconds(1));
        }
    }
    
    [Fact]
    public async Task FetchPendingAsync_WhenMessagesPresent_ReturnsOnlyPendingReadyToDeliver()
    {
        var sut = CreateSut();
        var pendingReady = CreateMessage(state: 0, deliverAt: DateTimeOffset.UtcNow.AddMinutes(-5), correlationId: "corr1", causationId: "caus1");
        var fullPayload = new byte[10] { 0, 91, 49, 93, 0, 0, 0, 0, 0, 0 };
        var slicedPayload = new ReadOnlyMemory<byte>(fullPayload, 1, 3);
        pendingReady = pendingReady with { Payload = slicedPayload, Headers = slicedPayload };

        var pendingNotReady = CreateMessage(state: 0, deliverAt: DateTimeOffset.UtcNow.AddMinutes(10));
        var dispatched = CreateMessage(state: 2);
        var retryingReady = CreateMessage(state: 3, deliverAt: DateTimeOffset.UtcNow.AddMinutes(-5));

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        
        foreach(var m in new[] { pendingReady, pendingNotReady, dispatched, retryingReady })
        {
            await connection.ExecuteAsync(
            "INSERT INTO messages (id, type, correlation_id, causation_id, payload, headers_json, created_at, updated_at, deliver_at, state) VALUES (@Id, @MessageType, @CorrelationId, @CausationId, @PayloadBytes, @HeadersBytes, @CreatedAt, @UpdatedAt, @DeliverAt, @State)", 
            new { Id = m.Id, m.MessageType, m.CorrelationId, m.CausationId, PayloadBytes = m.Payload.ToArray(), HeadersBytes = m.Headers.ToArray(), CreatedAt = m.CreatedAt.ToString("O"), UpdatedAt = DateTimeOffset.UtcNow.ToString("O"), DeliverAt = m.DeliverAt?.ToString("O"), State = m.Status });
        }

        var fetched = await sut.FetchPendingAsync(10);
        fetched.Should().HaveCount(2);
    }
    
    [Fact]
    public async Task BatchOperations_WhenCollectionsEmpty_CompletesWithoutModifyingDatabase()
    {
        var sut = CreateSut();
        var emptyMsgs = Array.Empty<OutboxMessage>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();

        var actInsert = async () => await sut.InsertBatchAsync(emptyMsgs, new EricksonLopez.Outbox.Persistence.DbTransactionContext(tx));
        var actDispatch = async () => await sut.MarkAsDispatchedAsync(emptyMsgs);
        var actFailed = async () => await sut.MarkAsFailedAsync(emptyMsgs, "error");

        await actInsert.Should().NotThrowAsync();
        await actDispatch.Should().NotThrowAsync();
        await actFailed.Should().NotThrowAsync();

        var messageCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM messages", transaction: tx);
        messageCount.Should().Be(0);
    }

    [Fact]
    public async Task ReclaimStaleMessagesAsync_WhenStaleMessagesExist_RevertsStateToPending()
    {
        var sut = CreateSut();
        var staleMsg = CreateMessage(state: 1);
        var freshMsg = CreateMessage(state: 1);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        
        await connection.ExecuteAsync("INSERT INTO messages (id, type, payload, headers_json, created_at, updated_at, state) VALUES (@Id, @MessageType, @P, @H, @CreatedAt, @UpdatedAt, @State)", new { Id = staleMsg.Id.ToString(), staleMsg.MessageType, P = Array.Empty<byte>(), H = Array.Empty<byte>(), CreatedAt = staleMsg.CreatedAt.ToString("O"), UpdatedAt = DateTimeOffset.UtcNow.AddHours(-2).ToString("O"), State = (int)staleMsg.Status });
        await connection.ExecuteAsync("INSERT INTO messages (id, type, payload, headers_json, created_at, updated_at, state) VALUES (@Id, @MessageType, @P, @H, @CreatedAt, @UpdatedAt, @State)", new { Id = freshMsg.Id.ToString(), freshMsg.MessageType, P = Array.Empty<byte>(), H = Array.Empty<byte>(), CreatedAt = freshMsg.CreatedAt.ToString("O"), UpdatedAt = DateTimeOffset.UtcNow.ToString("O"), State = (int)freshMsg.Status });

        var reclaimedCount = await sut.ReclaimStaleMessagesAsync(TimeSpan.FromHours(1));

        reclaimedCount.Should().Be(1);

        var staleState = await connection.QuerySingleAsync<int>("SELECT state FROM messages WHERE id = @Id", new { Id = staleMsg.Id.ToString() });
        var freshState = await connection.QuerySingleAsync<int>("SELECT state FROM messages WHERE id = @Id", new { Id = freshMsg.Id.ToString() });

        staleState.Should().Be(0);
        freshState.Should().Be(1);
    }

    [Fact]
    public async Task Operations_WithOutboxMessagesTableName_Succeeds()
    {
        var opt = new OutboxRuntimeOptions { TableName = "outbox_messages" };
        var sut = new SqliteOutboxRepository(() => new SqliteConnection(_connectionString), Microsoft.Extensions.Options.Options.Create(opt));
        var msg = CreateMessage();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(@"CREATE TABLE IF NOT EXISTS outbox_messages (
            id TEXT PRIMARY KEY,
            type TEXT NOT NULL,
            payload BLOB,
            correlation_id TEXT,
            causation_id TEXT,
            headers_json BLOB,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            processed_at TEXT,
            deliver_at TEXT,
            state INTEGER NOT NULL,
            retry_count INTEGER NOT NULL DEFAULT 0,
            owner_id TEXT,
            error TEXT
        );");
        await using var tx = await connection.BeginTransactionAsync();

        await sut.InsertAsync(msg, new DbTransactionContext(tx));
        await tx.CommitAsync();

        var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM outbox_messages WHERE id = @Id", new { Id = msg.Id.ToString() });
        count.Should().Be(1);
    }

    [Theory]
    [InlineData(false, 3)]
    [InlineData(true, 4)]
    public async Task MarkAsFailedAsync_WhenCalled_UpdatesStatusAndIncrementsRetryCount(bool isDeadLetter, int expectedState)
    {
        var sut = CreateSut();
        var msg1 = CreateMessage(state: 1, retryCount: 0);
        var msg2 = CreateMessage(state: 1, retryCount: 2);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("INSERT INTO messages (id, type, payload, headers_json, created_at, updated_at, state, retry_count) VALUES (@Id, @MessageType, @P, @H, @CreatedAt, @UpdatedAt, @State, @RetryCount)", new { Id = msg1.Id.ToString(), msg1.MessageType, P = Array.Empty<byte>(), H = Array.Empty<byte>(), CreatedAt = msg1.CreatedAt.ToString("O"), UpdatedAt = DateTimeOffset.UtcNow.ToString("O"), State = (int)msg1.Status, msg1.RetryCount });
        await connection.ExecuteAsync("INSERT INTO messages (id, type, payload, headers_json, created_at, updated_at, state, retry_count) VALUES (@Id, @MessageType, @P, @H, @CreatedAt, @UpdatedAt, @State, @RetryCount)", new { Id = msg2.Id.ToString(), msg2.MessageType, P = Array.Empty<byte>(), H = Array.Empty<byte>(), CreatedAt = msg2.CreatedAt.ToString("O"), UpdatedAt = DateTimeOffset.UtcNow.ToString("O"), State = (int)msg2.Status, msg2.RetryCount });

        await sut.MarkAsFailedAsync(new[] { msg1, msg2 }, "error", isDeadLetter);

        var db1 = await connection.QuerySingleAsync<(int state, int retry_count, string error)>("SELECT state, retry_count, error FROM messages WHERE id = @Id", new { Id = msg1.Id.ToString() });
        db1.state.Should().Be(expectedState);
        db1.retry_count.Should().Be(1);
        db1.error.Should().Be("error");

        var db2 = await connection.QuerySingleAsync<(int state, int retry_count, string error)>("SELECT state, retry_count, error FROM messages WHERE id = @Id", new { Id = msg2.Id.ToString() });
        db2.state.Should().Be(expectedState);
        db2.retry_count.Should().Be(3);
    }

    [Fact]
    public async Task MarkAsDispatchedAsync_WhenCalled_DeletesDispatchedMessages()
    {
        var sut = CreateSut();
        var msg1 = CreateMessage(state: 1);
        var msg2 = CreateMessage(state: 1);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync("INSERT INTO messages (id, type, payload, headers_json, created_at, updated_at, state) VALUES (@Id, @MessageType, @P, @H, @CreatedAt, @UpdatedAt, @State)", new { Id = msg1.Id.ToString(), msg1.MessageType, P = Array.Empty<byte>(), H = Array.Empty<byte>(), CreatedAt = msg1.CreatedAt.ToString("O"), UpdatedAt = DateTimeOffset.UtcNow.ToString("O"), State = (int)msg1.Status });
        await connection.ExecuteAsync("INSERT INTO messages (id, type, payload, headers_json, created_at, updated_at, state) VALUES (@Id, @MessageType, @P, @H, @CreatedAt, @UpdatedAt, @State)", new { Id = msg2.Id.ToString(), msg2.MessageType, P = Array.Empty<byte>(), H = Array.Empty<byte>(), CreatedAt = msg2.CreatedAt.ToString("O"), UpdatedAt = DateTimeOffset.UtcNow.ToString("O"), State = (int)msg2.Status });

        await sut.MarkAsDispatchedAsync(new[] { msg1, msg2 });

        var count1 = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM messages WHERE id IN (@Id1, @Id2)", new { Id1 = msg1.Id.ToString(), Id2 = msg2.Id.ToString() });
        count1.Should().Be(0); // Deletes dispatched
    }

    [Fact]
    public async Task InsertBatchAsync_WhenValidMessages_PersistsAllMessages()
    {
        var sut = CreateSut();
        var msg1 = CreateMessage(correlationId: "c1", causationId: "c2", deliverAt: DateTimeOffset.UtcNow.AddMinutes(5));
        var msg2 = CreateMessage(deliverAt: null) with { CorrelationId = null, CausationId = null };
        var messages = new[] { msg1, msg2 };

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var tx = connection.BeginTransaction();
        
        await sut.InsertBatchAsync(messages, new EricksonLopez.Outbox.Persistence.DbTransactionContext(tx));
        await tx.CommitAsync();

        var list = (await connection.QueryAsync<DbRow>("SELECT id as Id, correlation_id as CorrelationId, causation_id as CausationId, deliver_at as DeliverAt FROM messages")).ToList();
        list.Should().HaveCount(2);

        var db1 = list.Single(x => Guid.Parse(x.Id) == msg1.Id);
        db1.CorrelationId.Should().Be("c1");
        db1.CausationId.Should().Be("c2");
        
        var parsedDate = DateTimeOffset.Parse(db1.DeliverAt!, null, System.Globalization.DateTimeStyles.RoundtripKind);
        parsedDate.Should().BeCloseTo(msg1.DeliverAt!.Value, TimeSpan.FromMilliseconds(1));
    }

    private sealed class DbRow
    {
        public string Id { get; set; } = null!;
        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public string? DeliverAt { get; set; }
    }

    [Fact]
    public async Task GetPendingCountAsync_WhenCalled_ReturnsPendingCount()
    {
        var sut = CreateSut();
        var count = await sut.GetPendingCountAsync(CancellationToken.None);
        count.Should().Be(0);
    }
    
    [Fact]
    public async Task InsertBatchAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        var sut = CreateSut();
        var messages = new[] { CreateMessage() };
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var tx = connection.BeginTransaction();

        Func<Task> act = async () => await sut.InsertBatchAsync(messages, new EricksonLopez.Outbox.Persistence.DbTransactionContext(tx), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task FetchPendingAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        var sut = CreateSut();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = async () => await sut.FetchPendingAsync(10, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetPendingCountAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        var sut = CreateSut();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = async () => await sut.GetPendingCountAsync(cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task FetchPendingAsync_WhenNullableFieldsNull_MapsPropertiesAsNull()
    {
        var sut = CreateSut();
        var msg = CreateMessage(state: 0, deliverAt: null, correlationId: null, causationId: null);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "INSERT INTO messages (id, type, correlation_id, causation_id, payload, headers_json, created_at, updated_at, processed_at, deliver_at, state, owner_id, error) VALUES (@Id, @MessageType, NULL, NULL, @PayloadBytes, @HeadersBytes, @CreatedAt, @UpdatedAt, NULL, NULL, @State, NULL, NULL)", 
            new { Id = msg.Id.ToString(), msg.MessageType, PayloadBytes = msg.Payload.ToArray(), HeadersBytes = msg.Headers.ToArray(), CreatedAt = msg.CreatedAt.ToString("O"), UpdatedAt = DateTimeOffset.UtcNow.ToString("O"), State = msg.Status });

        var fetched = await sut.FetchPendingAsync(10);
        
        fetched.Should().HaveCount(1);
        var f = fetched[0];
        f.CorrelationId.Should().BeNull();
        f.CausationId.Should().BeNull();
        f.DeliverAt.Should().BeNull();
    }
    
    [Fact]
    public async Task FetchPendingAsync_WhenFieldsNonNull_MapsAllPropertiesCorrectly()
    {
        var sut = CreateSut();
        var msg = CreateMessage(state: 0, deliverAt: DateTimeOffset.UtcNow.AddMinutes(-10), correlationId: "c1", causationId: "c2");

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "INSERT INTO messages (id, type, correlation_id, causation_id, payload, headers_json, created_at, updated_at, processed_at, deliver_at, state, owner_id, error) VALUES (@Id, @MessageType, @CorrelationId, @CausationId, @PayloadBytes, @HeadersBytes, @CreatedAt, @UpdatedAt, @ProcessedAt, @DeliverAt, @State, @OwnerId, @Error)", 
            new { Id = msg.Id.ToString(), msg.MessageType, msg.CorrelationId, msg.CausationId, PayloadBytes = msg.Payload.ToArray(), HeadersBytes = msg.Headers.ToArray(), CreatedAt = msg.CreatedAt.ToString("O"), UpdatedAt = DateTimeOffset.UtcNow.ToString("O"), ProcessedAt = DateTimeOffset.UtcNow.ToString("O"), DeliverAt = msg.DeliverAt!.Value.ToString("O"), State = msg.Status, OwnerId = "owner1", Error = "some error" });

        var fetched = await sut.FetchPendingAsync(10);
        
        fetched.Should().HaveCount(1);
        var f = fetched[0];
        f.CorrelationId.Should().Be("c1");
        f.CausationId.Should().Be("c2");
        f.DeliverAt.Should().NotBeNull();
    }

    [Fact]
    public async Task PurgeDispatchedMessagesAsync_WhenCutoffReached_DeletesDispatchedMessages()
    {
        var sut = CreateSut();
        var cutoff = DateTimeOffset.UtcNow.AddDays(-7);

        var msgToPurge = CreateMessage(state: 2, createdAt: cutoff.AddDays(-2));
        var msgToKeepNewer = CreateMessage(state: 2, createdAt: cutoff.AddDays(1));
        var msgToKeepPending = CreateMessage(state: 0, createdAt: cutoff.AddDays(-2));

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "INSERT INTO messages (id, type, payload, correlation_id, causation_id, headers_json, created_at, updated_at, processed_at, state) VALUES (@Id, @Type, @Payload, @CorrelationId, @CausationId, @HeadersJson, @CreatedAt, @UpdatedAt, @ProcessedAt, @State)",
            new[]
            {
                new { Id = msgToPurge.Id.ToString(), Type = "A", Payload = msgToPurge.Payload.ToArray(), msgToPurge.CorrelationId, msgToPurge.CausationId, HeadersJson = msgToPurge.Headers.ToArray(), CreatedAt = cutoff.AddDays(-2).ToString("O"), UpdatedAt = cutoff.AddDays(-1).ToString("O"), ProcessedAt = (string?)cutoff.AddDays(-1).ToString("O"), State = 2 },
                new { Id = msgToKeepNewer.Id.ToString(), Type = "B", Payload = msgToKeepNewer.Payload.ToArray(), msgToKeepNewer.CorrelationId, msgToKeepNewer.CausationId, HeadersJson = msgToKeepNewer.Headers.ToArray(), CreatedAt = cutoff.AddDays(1).ToString("O"), UpdatedAt = cutoff.AddDays(2).ToString("O"), ProcessedAt = (string?)cutoff.AddDays(2).ToString("O"), State = 2 },
                new { Id = msgToKeepPending.Id.ToString(), Type = "C", Payload = msgToKeepPending.Payload.ToArray(), msgToKeepPending.CorrelationId, msgToKeepPending.CausationId, HeadersJson = msgToKeepPending.Headers.ToArray(), CreatedAt = cutoff.AddDays(-2).ToString("O"), UpdatedAt = cutoff.AddDays(-2).ToString("O"), ProcessedAt = (string?)null, State = 0 }
            });

        var purged = await sut.PurgeDispatchedMessagesAsync(cutoff, batchSize: 10);
        purged.Should().Be(1);

        var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM messages WHERE id = @Id", new { Id = msgToPurge.Id.ToString() });
        count.Should().Be(0);

        var keptCount = await connection.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM messages");
        keptCount.Should().Be(2);
    }

    [Fact]
    public async Task PurgeDispatchedMessagesAsync_WhenBatchSizeZero_ReturnsZero()
    {
        var sut = CreateSut();
        var purged = await sut.PurgeDispatchedMessagesAsync(DateTimeOffset.UtcNow, batchSize: 0);
        purged.Should().Be(0);
    }

    [Fact]
    public async Task ConcurrentOperations_WhenExecutedInParallel_MaintainsDataIntegrity()
    {
        var sut = CreateSut();
        int workerCount = 5;
        int messagesPerWorker = 20;
        var tasks = new Task[workerCount];
        var insertedIds = new System.Collections.Concurrent.ConcurrentBag<Guid>();

        for (int i = 0; i < workerCount; i++)
        {
            tasks[i] = Task.Run(async () =>
            {
                for (int j = 0; j < messagesPerWorker; j++)
                {
                    var msg = CreateMessage(state: 0, deliverAt: DateTimeOffset.UtcNow.AddMinutes(-1));
                    insertedIds.Add(msg.Id);

                    await using var conn = new SqliteConnection(_connectionString);
                    await conn.OpenAsync();
                    await using var tx = await conn.BeginTransactionAsync();

                    await sut.InsertAsync(msg, new EricksonLopez.Outbox.Persistence.DbTransactionContext(tx));
                    await tx.CommitAsync();
                }
            });
        }

        await Task.WhenAll(tasks);

        var pending = await sut.FetchPendingAsync(workerCount * messagesPerWorker * 2);
        pending.Count.Should().Be(workerCount * messagesPerWorker);
        insertedIds.Distinct().Count().Should().Be(workerCount * messagesPerWorker);
    }

    [Fact]
    public async Task InsertBatchAsync_WithCustomTableName_Succeeds()
    {
        var opt = new OutboxRuntimeOptions { TableName = "custom_outbox" };
        var sut = new SqliteOutboxRepository(() => new SqliteConnection(_connectionString), Microsoft.Extensions.Options.Options.Create(opt));
        var msg = CreateMessage(correlationId: "c1", causationId: "c2", deliverAt: DateTimeOffset.UtcNow.AddMinutes(5));

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(@"CREATE TABLE IF NOT EXISTS ""custom_outbox"" (
            id TEXT PRIMARY KEY,
            type TEXT NOT NULL,
            payload BLOB,
            correlation_id TEXT,
            causation_id TEXT,
            headers_json BLOB,
            created_at TEXT NOT NULL,
            updated_at TEXT NOT NULL,
            processed_at TEXT,
            deliver_at TEXT,
            state INTEGER NOT NULL,
            retry_count INTEGER NOT NULL DEFAULT 0,
            owner_id TEXT,
            error TEXT
        );");
        await using var tx = await connection.BeginTransactionAsync();

        await sut.InsertBatchAsync(new[] { msg }, new DbTransactionContext(tx));
        await tx.CommitAsync();

        var count = await connection.ExecuteScalarAsync<int>(@"SELECT COUNT(1) FROM ""custom_outbox"" WHERE id = @Id", new { Id = msg.Id.ToString() });
        count.Should().Be(1);
    }

    [Fact]
    public async Task InsertBatchAsync_WhenDuplicateId_IgnoresWithoutThrowing()
    {
        var sut = CreateSut();
        var msg = CreateMessage();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();

        await sut.InsertBatchAsync(new[] { msg }, new DbTransactionContext(tx));
        await sut.InsertBatchAsync(new[] { msg }, new DbTransactionContext(tx));
        await tx.CommitAsync();

        var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM messages WHERE id = @Id", new { Id = msg.Id.ToString() });
        count.Should().Be(1);
    }

    [Fact]
    public async Task PurgeDispatchedMessagesAsync_WhenBatchSizeNegative_ReturnsZero()
    {
        var sut = CreateSut();
        var purged = await sut.PurgeDispatchedMessagesAsync(DateTimeOffset.UtcNow, batchSize: -1);
        purged.Should().Be(0);
    }
}












