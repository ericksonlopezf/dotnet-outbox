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
using EricksonLopez.Outbox.Storage.MySql;
using MySqlConnector;
using Testcontainers.MySql;
using Xunit;

namespace EricksonLopez.Outbox.Tests;

public class MySqlContainerFixture : IAsyncLifetime
{
    public MySqlContainer Container { get; } = new MySqlBuilder().WithDatabase("testdb").WithUsername("root").WithPassword("password").WithCommand("--local-infile=1").Build();

    public Task InitializeAsync() => Container.StartAsync();
    public Task DisposeAsync() => Container.DisposeAsync().AsTask();
}

public class MySqlOutboxRepositoryTests : IClassFixture<MySqlContainerFixture>, IAsyncLifetime
{
    private readonly MySqlContainerFixture _fixture;
    private readonly IFixture _autoFixture;
    private readonly EricksonLopez.Outbox.OutboxRuntimeOptions _options = new() { SchemaName = "testdb", TableName = "outbox_messages" };
    public string InstanceId => _options.InstanceId;

    public MySqlOutboxRepositoryTests(MySqlContainerFixture fixture)
    {
        _fixture = fixture;
        _autoFixture = new Fixture().Customize(new AutoNSubstituteCustomization());
    }

    public async Task InitializeAsync()
    {
        using var connection = new MySqlConnection(_fixture.Container.GetConnectionString() + ";AllowLoadLocalInfile=true");
        await connection.OpenAsync();

        const string schema = @"
            CREATE TABLE IF NOT EXISTS outbox_messages (
                id VARCHAR(36) PRIMARY KEY,
                type VARCHAR(255) NOT NULL,
                payload LONGBLOB,
                correlation_id VARCHAR(255),
                causation_id VARCHAR(255),
                headers_json LONGBLOB,
                created_at DATETIME(6) NOT NULL,
                updated_at DATETIME(6) NOT NULL,
                processed_at DATETIME(6),
                deliver_at DATETIME(6),
                state INT NOT NULL,
                retry_count INT NOT NULL DEFAULT 0,
                owner_id VARCHAR(255),
                error LONGTEXT
            );
            TRUNCATE TABLE outbox_messages;";
        
        await connection.ExecuteAsync(schema);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private MySqlOutboxRepository CreateSut()
    {
        return new MySqlOutboxRepository(() => new MySqlConnection(_fixture.Container.GetConnectionString() + ";AllowLoadLocalInfile=true"), Microsoft.Extensions.Options.Options.Create(_options));
    }

    private OutboxMessage CreateMessage(int state = 0, DateTimeOffset? deliverAt = null, DateTimeOffset? createdAt = null, int retryCount = 0, string? correlationId = null, string? causationId = null)
    {
        var msg = _autoFixture.Create<OutboxMessage>();
        return msg with 
        { 
            Status = (EricksonLopez.Outbox.OutboxMessageStatus)state, 
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

        await using var connection = new MySqlConnection(_fixture.Container.GetConnectionString() + ";AllowLoadLocalInfile=true");
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        
        await sut.InsertAsync(msg, new EricksonLopez.Outbox.Persistence.DbTransactionContext(tx));
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
    public async Task FetchPendingAsync_Should_Return_Only_Pending_Messages_Ready_To_Deliver()
    {
        var sut = CreateSut();
        var pendingReady = CreateMessage(state: 0, deliverAt: DateTimeOffset.UtcNow.AddMinutes(-5), correlationId: "corr1", causationId: "caus1");
        var fullPayload = new byte[10] { 0, 91, 49, 93, 0, 0, 0, 0, 0, 0 }; // "[1]"
        var slicedPayload = new ReadOnlyMemory<byte>(fullPayload, 1, 3);
        pendingReady = pendingReady with { Payload = slicedPayload, Headers = slicedPayload };

        var pendingNotReady = CreateMessage(state: 0, deliverAt: DateTimeOffset.UtcNow.AddMinutes(10));
        var dispatched = CreateMessage(state: 2);
        var retryingReady = CreateMessage(state: 3, deliverAt: DateTimeOffset.UtcNow.AddMinutes(-5));

        await using var connection = new MySqlConnection(_fixture.Container.GetConnectionString() + ";AllowLoadLocalInfile=true");
        await connection.OpenAsync();
        
        foreach(var m in new[] { pendingReady, pendingNotReady, dispatched, retryingReady })
        {
            await connection.ExecuteAsync(
            "INSERT INTO outbox_messages (id, type, correlation_id, causation_id, payload, headers_json, created_at, updated_at, deliver_at, state, owner_id) VALUES (@Id, @MessageType, @CorrelationId, @CausationId, @PayloadBytes, @HeadersBytes, @CreatedAt, UTC_TIMESTAMP(6), @DeliverAt, @State, @OwnerId)", 
            new { Id = m.Id.ToString(), m.MessageType, m.CorrelationId, m.CausationId, PayloadBytes = m.Payload.ToArray(), HeadersBytes = m.Headers.ToArray(), CreatedAt = m.CreatedAt.UtcDateTime, DeliverAt = m.DeliverAt?.UtcDateTime, State = m.Status, OwnerId = InstanceId });
        }

        var fetched = await sut.FetchPendingAsync(10);
        fetched.Should().HaveCount(2);
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
        await using var connection = new MySqlConnection(_fixture.Container.GetConnectionString() + ";AllowLoadLocalInfile=true");
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

        await using var connection = new MySqlConnection(_fixture.Container.GetConnectionString() + ";AllowLoadLocalInfile=true");
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
        var msg = CreateMessage(state: 1, retryCount: 0);

        await using var connection = new MySqlConnection(_fixture.Container.GetConnectionString() + ";AllowLoadLocalInfile=true");
        await connection.OpenAsync();
        await connection.ExecuteAsync("INSERT INTO outbox_messages (id, type, payload, headers_json, created_at, updated_at, state, retry_count, owner_id) VALUES (@Id, @MessageType, '{}', '{}', @CreatedAt, UTC_TIMESTAMP(6), @State, @RetryCount, @OwnerId)", new { Id = msg.Id.ToString(), msg.MessageType, msg.CreatedAt, State = msg.Status, msg.RetryCount, OwnerId = InstanceId });

        await sut.MarkAsFailedAsync(new[] { msg }, "error", isDeadLetter);

        var db1 = await connection.QuerySingleAsync<(int state, int retry_count, string error)>("SELECT state, retry_count, error FROM outbox_messages WHERE id = @Id", new { Id = msg.Id.ToString() });
        db1.state.Should().Be(expectedState);
        db1.retry_count.Should().Be(1);
        db1.error.Should().Be("error");
    }

    [Fact]
    public async Task MarkAsDispatchedAsync_Should_Update_Status_To_Dispatched()
    {
        var sut = CreateSut();
        var msg = CreateMessage(state: 1);

        await using var connection = new MySqlConnection(_fixture.Container.GetConnectionString() + ";AllowLoadLocalInfile=true");
        await connection.OpenAsync();
        await connection.ExecuteAsync("INSERT INTO outbox_messages (id, type, payload, headers_json, created_at, updated_at, state, owner_id) VALUES (@Id, @MessageType, '{}', '{}', @CreatedAt, UTC_TIMESTAMP(6), @State, @OwnerId)", new { Id = msg.Id.ToString(), msg.MessageType, msg.CreatedAt, State = msg.Status, OwnerId = InstanceId });

        await sut.MarkAsDispatchedAsync(new[] { msg });

        var count1 = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM outbox_messages WHERE id = @Id", new { Id = msg.Id.ToString() });
        count1.Should().Be(0);
    }

    [Fact]
    public async Task InsertBatchAsync_Should_Persist_Multiple_Messages()
    {
        var sut = CreateSut();
        var msg1 = CreateMessage(correlationId: "c1", causationId: "c2", deliverAt: DateTimeOffset.UtcNow.AddMinutes(5));
        var msg2 = CreateMessage(deliverAt: null) with { CorrelationId = null, CausationId = null };
        var messages = new[] { msg1, msg2 };

        await using var connection = new MySqlConnection(_fixture.Container.GetConnectionString() + ";AllowLoadLocalInfile=true");
        await connection.OpenAsync();
        await using var tx = connection.BeginTransaction();
        
        await sut.InsertBatchAsync(messages, new EricksonLopez.Outbox.Persistence.DbTransactionContext(tx));
        await tx.CommitAsync();

        var list = (await connection.QueryAsync("SELECT id, correlation_id, causation_id, deliver_at FROM outbox_messages")).ToList();
        list.Should().HaveCount(2);
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

        await using var connection = new MySqlConnection(_fixture.Container.GetConnectionString() + ";AllowLoadLocalInfile=true");
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
    public async Task FetchPendingAsync_Should_Map_Null_Fields_Correctly()
    {
        var sut = CreateSut();
        var msg = CreateMessage(state: 0, deliverAt: null, correlationId: null, causationId: null);

        await using var connection = new MySqlConnection(_fixture.Container.GetConnectionString() + ";AllowLoadLocalInfile=true");
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
    }
    
    [Fact]
    public async Task FetchPendingAsync_Should_Map_NonNull_Fields_Correctly()
    {
        var sut = CreateSut();
        var msg = CreateMessage(state: 0, deliverAt: DateTimeOffset.UtcNow.AddMinutes(-10), correlationId: "c1", causationId: "c2");

        await using var connection = new MySqlConnection(_fixture.Container.GetConnectionString() + ";AllowLoadLocalInfile=true");
        await connection.OpenAsync();
        await connection.ExecuteAsync(
            "INSERT INTO outbox_messages (id, type, correlation_id, causation_id, payload, headers_json, created_at, updated_at, processed_at, deliver_at, state, owner_id) VALUES (@Id, @MessageType, @CorrelationId, @CausationId, @PayloadBytes, @HeadersBytes, @CreatedAt, UTC_TIMESTAMP(6), @ProcessedAt, @DeliverAt, @State, @OwnerId)", 
            new { Id = msg.Id.ToString(), msg.MessageType, msg.CorrelationId, msg.CausationId, PayloadBytes = msg.Payload.ToArray(), HeadersBytes = msg.Headers.ToArray(), CreatedAt = msg.CreatedAt.UtcDateTime, ProcessedAt = DateTime.UtcNow, DeliverAt = msg.DeliverAt!.Value.UtcDateTime, State = msg.Status, OwnerId = "owner1" });

        var fetched = await sut.FetchPendingAsync(10);
        
        fetched.Should().HaveCount(1);
        var f = fetched[0];
        f.CorrelationId.Should().Be("c1");
        f.CausationId.Should().Be("c2");
        f.DeliverAt.Should().NotBeNull();
    }
}














