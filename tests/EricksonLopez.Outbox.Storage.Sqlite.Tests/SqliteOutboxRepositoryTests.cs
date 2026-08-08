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
using EricksonLopez.Outbox.Storage.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

namespace EricksonLopez.Outbox.Tests;

public class SqliteOutboxRepositoryTests : IDisposable
{
    private readonly IFixture _autoFixture;
    private readonly SqliteConnection _connection;

    public SqliteOutboxRepositoryTests()
    {
        _autoFixture = new Fixture().Customize(new AutoNSubstituteCustomization());
        
        // In-memory sqlite shared cache so connections to "Data Source=outboxtests;Mode=Memory;Cache=Shared" share DB.
        _connection = new SqliteConnection("Data Source=outboxtests;Mode=Memory;Cache=Shared");
        _connection.Open();

        const string schema = @"
            CREATE TABLE IF NOT EXISTS messages (
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
            );
            DELETE FROM messages;";
        
        _connection.Execute(schema);
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private static SqliteOutboxRepository CreateSut()
    {
        return new SqliteOutboxRepository(() => new SqliteConnection("Data Source=outboxtests;Mode=Memory;Cache=Shared"));
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

        await using var connection = new SqliteConnection("Data Source=outboxtests;Mode=Memory;Cache=Shared");
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

        await using var connection = new SqliteConnection("Data Source=outboxtests;Mode=Memory;Cache=Shared");
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
    public async Task Empty_Collections_Should_Return_Immediately()
    {
        var sut = CreateSut();
        var emptyMsgs = Array.Empty<OutboxMessage>();
        await using var connection = new SqliteConnection("Data Source=outboxtests;Mode=Memory;Cache=Shared");
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

        await using var connection = new SqliteConnection("Data Source=outboxtests;Mode=Memory;Cache=Shared");
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

    [Theory]
    [InlineData(false, 3)]
    [InlineData(true, 4)]
    public async Task MarkAsFailedAsync_Should_Update_Status_And_Increment_Retry(bool isDeadLetter, int expectedState)
    {
        var sut = CreateSut();
        var msg = CreateMessage(state: 1, retryCount: 0);

        await using var connection = new SqliteConnection("Data Source=outboxtests;Mode=Memory;Cache=Shared");
        await connection.OpenAsync();
        await connection.ExecuteAsync("INSERT INTO messages (id, type, payload, headers_json, created_at, updated_at, state, retry_count) VALUES (@Id, @MessageType, @P, @H, @CreatedAt, @UpdatedAt, @State, @RetryCount)", new { Id = msg.Id.ToString(), msg.MessageType, P = Array.Empty<byte>(), H = Array.Empty<byte>(), CreatedAt = msg.CreatedAt.ToString("O"), UpdatedAt = DateTimeOffset.UtcNow.ToString("O"), State = (int)msg.Status, msg.RetryCount });

        await sut.MarkAsFailedAsync(new[] { msg }, "error", isDeadLetter);

        var db1 = await connection.QuerySingleAsync<(int state, int retry_count, string error)>("SELECT state, retry_count, error FROM messages WHERE id = @Id", new { Id = msg.Id.ToString() });
        db1.state.Should().Be(expectedState);
        db1.retry_count.Should().Be(1);
        db1.error.Should().Be("error");
    }

    [Fact]
    public async Task MarkAsDispatchedAsync_Should_Update_Status_To_Dispatched()
    {
        var sut = CreateSut();
        var msg = CreateMessage(state: 1);

        await using var connection = new SqliteConnection("Data Source=outboxtests;Mode=Memory;Cache=Shared");
        await connection.OpenAsync();
        await connection.ExecuteAsync("INSERT INTO messages (id, type, payload, headers_json, created_at, updated_at, state) VALUES (@Id, @MessageType, @P, @H, @CreatedAt, @UpdatedAt, @State)", new { Id = msg.Id.ToString(), msg.MessageType, P = Array.Empty<byte>(), H = Array.Empty<byte>(), CreatedAt = msg.CreatedAt.ToString("O"), UpdatedAt = DateTimeOffset.UtcNow.ToString("O"), State = (int)msg.Status });

        await sut.MarkAsDispatchedAsync(new[] { msg });

        var count1 = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM messages WHERE id = @Id", new { Id = msg.Id.ToString() });
        count1.Should().Be(0); // Deletes dispatched
    }

    [Fact]
    public async Task InsertBatchAsync_Should_Persist_Multiple_Messages()
    {
        var sut = CreateSut();
        var msg1 = CreateMessage(correlationId: "c1", causationId: "c2", deliverAt: DateTimeOffset.UtcNow.AddMinutes(5));
        var msg2 = CreateMessage(deliverAt: null) with { CorrelationId = null, CausationId = null };
        var messages = new[] { msg1, msg2 };

        await using var connection = new SqliteConnection("Data Source=outboxtests;Mode=Memory;Cache=Shared");
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
    public async Task GetPendingCountAsync_Should_Return_Count()
    {
        var sut = CreateSut();
        var count = await sut.GetPendingCountAsync(CancellationToken.None);
        count.Should().Be(0);
    }
    
    [Fact]
    public async Task InsertBatchAsync_Should_Check_Cancellation_Token()
    {
        var sut = CreateSut();
        var messages = new[] { CreateMessage() };
        var cts = new CancellationTokenSource();
        cts.Cancel();

        await using var connection = new SqliteConnection("Data Source=outboxtests;Mode=Memory;Cache=Shared");
        await connection.OpenAsync();
        await using var tx = connection.BeginTransaction();

        Func<Task> act = async () => await sut.InsertBatchAsync(messages, new EricksonLopez.Outbox.Persistence.DbTransactionContext(tx), cts.Token);
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
    public async Task GetPendingCountAsync_Should_Check_Cancellation_Token()
    {
        var sut = CreateSut();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = async () => await sut.GetPendingCountAsync(cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task FetchPendingAsync_Should_Map_Null_Fields_Correctly()
    {
        var sut = CreateSut();
        var msg = CreateMessage(state: 0, deliverAt: null, correlationId: null, causationId: null);

        await using var connection = new SqliteConnection("Data Source=outboxtests;Mode=Memory;Cache=Shared");
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
    public async Task FetchPendingAsync_Should_Map_NonNull_Fields_Correctly()
    {
        var sut = CreateSut();
        var msg = CreateMessage(state: 0, deliverAt: DateTimeOffset.UtcNow.AddMinutes(-10), correlationId: "c1", causationId: "c2");

        await using var connection = new SqliteConnection("Data Source=outboxtests;Mode=Memory;Cache=Shared");
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
}









