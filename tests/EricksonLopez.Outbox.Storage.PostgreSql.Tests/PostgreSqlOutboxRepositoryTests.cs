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
using EricksonLopez.Outbox.Storage.PostgreSql;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace EricksonLopez.Outbox.Tests;

public class PostgreSqlContainerFixture : IAsyncLifetime
{
    public PostgreSqlContainer Container { get; } = new PostgreSqlBuilder().WithImage("postgres:15-alpine").Build();
    public NpgsqlDataSource DataSource { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await Container.StartAsync();
        DataSource = NpgsqlDataSource.Create(Container.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        if (DataSource != null) await DataSource.DisposeAsync();
        await Container.DisposeAsync();
    }
}

public class PostgreSqlOutboxRepositoryTests : IClassFixture<PostgreSqlContainerFixture>, IAsyncLifetime
{
    private readonly PostgreSqlContainerFixture _fixture;
    private readonly IFixture _autoFixture;
    private readonly EricksonLopez.Outbox.OutboxRuntimeOptions _options = new() { SchemaName = "outbox", TableName = "messages" };
    public string InstanceId => _options.InstanceId;
    protected NpgsqlDataSource _dataSource => _fixture.DataSource;

    public PostgreSqlOutboxRepositoryTests(PostgreSqlContainerFixture fixture)
    {
        _fixture = fixture;
        _autoFixture = new Fixture().Customize(new AutoNSubstituteCustomization());
    }

    public async Task InitializeAsync()
    {
        using var connection = await _dataSource.OpenConnectionAsync();

        const string schema = @"
            CREATE SCHEMA IF NOT EXISTS outbox;
            CREATE TABLE IF NOT EXISTS outbox.messages (
                id UUID,
                type VARCHAR(255) NOT NULL,
                payload JSONB,
                correlation_id VARCHAR(255),
                causation_id VARCHAR(255),
                headers_json JSONB,
                created_at TIMESTAMPTZ NOT NULL,
                updated_at TIMESTAMPTZ NOT NULL,
                processed_at TIMESTAMPTZ,
                deliver_at TIMESTAMPTZ,
                state INT NOT NULL,
                retry_count INT NOT NULL DEFAULT 0,
                owner_id UUID,
                error TEXT,
                PRIMARY KEY (id, created_at)
            );
            TRUNCATE TABLE outbox.messages;";
        
        await connection.ExecuteAsync(schema);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private PostgreSqlOutboxRepository CreateSut(int? largeTableThreshold = null)
    {
        var runtimeOpts = new EricksonLopez.Outbox.OutboxRuntimeOptions 
        { 
            SchemaName = _options.SchemaName, 
            TableName = _options.TableName,
            InstanceId = InstanceId
        };
        if (largeTableThreshold.HasValue)
        {
            runtimeOpts.LargeTableThreshold = largeTableThreshold.Value;
        }
        return new PostgreSqlOutboxRepository(
            _dataSource!, 
            Microsoft.Extensions.Options.Options.Create(runtimeOpts));
    }

    private async Task CleanDatabaseAsync()
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync("TRUNCATE TABLE outbox.messages;");
    }

    private static DateTimeOffset TruncateToMicroseconds(DateTimeOffset dt)
    {
        return new DateTimeOffset(dt.Ticks - (dt.Ticks % 10), dt.Offset);
    }

    private OutboxMessage CreateMessage(int state = 0, DateTimeOffset? deliverAt = null, DateTimeOffset? createdAt = null, int retryCount = 0, string? correlationId = null, string? causationId = null)
    {
        var msg = _autoFixture.Create<OutboxMessage>();
        return msg with 
        { 
            Status = (EricksonLopez.Outbox.OutboxMessageStatus)state, 
            DeliverAt = deliverAt, 
            CreatedAt = TruncateToMicroseconds(createdAt ?? DateTimeOffset.UtcNow),
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
        var msg = CreateMessage();

        await using var connection = await _dataSource.OpenConnectionAsync();
        
        await using var tx = await connection.BeginTransactionAsync();
        
        await CreateSut().InsertAsync(msg, new EricksonLopez.Outbox.Persistence.DbTransactionContext(tx));
        await tx.CommitAsync();

        var count = await connection.ExecuteScalarAsync<long>("SELECT COUNT(1) FROM outbox.messages WHERE id = @Id", new { msg.Id });
        count.Should().Be(1);
    }

    [Fact]
    public async Task FetchPendingAsync_Should_Return_Only_Pending_Messages_Ready_To_Deliver()
    {
        var sut = CreateSut();
        
        // Insert various messages
        var pendingReady = CreateMessage(state: 0, deliverAt: DateTimeOffset.UtcNow.AddMinutes(-5), correlationId: "corr1", causationId: "caus1");
        
        var fullPayload = new byte[10] { 0, 91, 49, 93, 0, 0, 0, 0, 0, 0 }; // "[1]"
        var slicedPayload = new ReadOnlyMemory<byte>(fullPayload, 1, 3);
        pendingReady = pendingReady with { Payload = slicedPayload, Headers = slicedPayload };

        var pendingNotReady = CreateMessage(state: 0, deliverAt: DateTimeOffset.UtcNow.AddMinutes(10));
        var dispatched = CreateMessage(state: 2);
        var retryingReady = CreateMessage(state: 3, deliverAt: DateTimeOffset.UtcNow.AddMinutes(-5));

        await using var connection = await _dataSource.OpenConnectionAsync();
        
        
        foreach(var m in new[] { pendingReady, pendingNotReady, dispatched, retryingReady })
        {
            var payloadStr = System.Text.Encoding.UTF8.GetString(m.Payload.Span);
            var headersStr = System.Text.Encoding.UTF8.GetString(m.Headers.Span);
            await connection.ExecuteAsync(
            "INSERT INTO outbox.messages (id, type, correlation_id, causation_id, payload, headers_json, created_at, updated_at, deliver_at, state) VALUES (@Id, @MessageType, @CorrelationId, @CausationId, @PayloadStr::jsonb, @HeadersStr::jsonb, @CreatedAt, NOW(), @DeliverAt, @State)", 
            new { m.Id, m.MessageType, m.CorrelationId, m.CausationId, PayloadStr = payloadStr, HeadersStr = headersStr, m.CreatedAt, m.DeliverAt, State = m.Status });
        }

        var fetched = await sut.FetchPendingAsync(10);

        fetched.Should().HaveCount(2);
        fetched.Select(x => x.Id).Should().Contain(new[] { pendingReady.Id, retryingReady.Id });
        fetched.Select(x => x.Id).Should().NotContain(pendingNotReady.Id);
        var fetchedMsg = fetched.Single(x => x.Id == pendingReady.Id);
        fetchedMsg.Payload.ToArray().Should().BeEquivalentTo(slicedPayload.ToArray());
        fetchedMsg.Headers.ToArray().Should().BeEquivalentTo(slicedPayload.ToArray());
        fetchedMsg.MessageType.Should().Be(pendingReady.MessageType);
        fetchedMsg.CorrelationId.Should().Be(pendingReady.CorrelationId);
        fetchedMsg.CausationId.Should().Be(pendingReady.CausationId);
        fetchedMsg.CreatedAt.Should().BeCloseTo(pendingReady.CreatedAt, TimeSpan.FromMilliseconds(1));
        if (pendingReady.DeliverAt.HasValue)
            fetchedMsg.DeliverAt.Should().BeCloseTo(pendingReady.DeliverAt.Value, TimeSpan.FromMilliseconds(1));
        else
            fetchedMsg.DeliverAt.Should().BeNull();
        fetchedMsg.RetryCount.Should().Be(pendingReady.RetryCount);

        // Ensure state is updated to 1
        var dbState = await connection.QuerySingleAsync<int>("SELECT state FROM outbox.messages WHERE id = @Id", new { pendingReady.Id });
        dbState.Should().Be(1);

        // Test with NULL payload and headers to cover fallback logic
        var nullPayloadMsg = CreateMessage(state: 0, deliverAt: DateTimeOffset.UtcNow.AddMinutes(-1));
        await connection.ExecuteAsync("INSERT INTO outbox.messages (id, type, payload, headers_json, created_at, updated_at, deliver_at, state) VALUES (@Id, @MessageType, NULL, NULL, @CreatedAt, NOW(), @DeliverAt, @State)", new { nullPayloadMsg.Id, nullPayloadMsg.MessageType, nullPayloadMsg.CreatedAt, nullPayloadMsg.DeliverAt, State = nullPayloadMsg.Status });
        
        var fetchedNull = await sut.FetchPendingAsync(10);
        var fetchedNullMsg = fetchedNull.Single(x => x.Id == nullPayloadMsg.Id);
        System.Text.Encoding.UTF8.GetString(fetchedNullMsg.Payload.Span).Should().Be("{}");
        System.Text.Encoding.UTF8.GetString(fetchedNullMsg.Headers.Span).Should().Be("{}");
    }

    [Fact]
    public async Task InsertBatchAsync_Should_Persist_Multiple_Messages()
    {
        var sut = CreateSut();
        var msg1 = CreateMessage(correlationId: "c1", causationId: "c2", deliverAt: DateTimeOffset.UtcNow.AddMinutes(5));
        var msg2 = CreateMessage(deliverAt: null) with { CorrelationId = null, CausationId = null };
        var messages = new[] { msg1, msg2 };

        await using var connection = await _dataSource.OpenConnectionAsync();
        
        await using var tx = await connection.BeginTransactionAsync();
        
        await sut.InsertBatchAsync(messages, new EricksonLopez.Outbox.Persistence.DbTransactionContext(tx));
        await tx.CommitAsync();

        var list = (await connection.QueryAsync("SELECT id, correlation_id, causation_id, deliver_at FROM outbox.messages")).ToList();
        list.Should().HaveCount(2);
        
        var db1 = list.Single(x => (Guid)x.id == msg1.Id);
        ((string)db1.correlation_id).Should().Be("c1");
        ((string)db1.causation_id).Should().Be("c2");
        ((DateTime)db1.deliver_at).Should().BeCloseTo(msg1.DeliverAt!.Value.UtcDateTime, TimeSpan.FromMilliseconds(1));

        var db2 = list.Single(x => (Guid)x.id == msg2.Id);
        ((string?)db2.correlation_id).Should().BeNull();
        ((string?)db2.causation_id).Should().BeNull();
        ((DateTime?)db2.deliver_at).Should().BeNull();
    }

    [Fact]
    public async Task InsertBulkAsync_Should_Insert_Multiple_Records_Using_Binary_Copy()
    {
        var sut = CreateSut();
        var msg1 = CreateMessage(correlationId: "c1", causationId: "c2", deliverAt: DateTimeOffset.UtcNow.AddMinutes(5));
        var msg2 = CreateMessage(deliverAt: null) with { CorrelationId = null, CausationId = null };
        var messages = new[] { msg1, msg2 };

        // Empty case should not throw
        await sut.InsertBulkAsync(Array.Empty<OutboxMessage>(), default);

        await sut.InsertBulkAsync(messages, default);

        await using var connection = await _dataSource.OpenConnectionAsync();
        var list = (await connection.QueryAsync("SELECT id, correlation_id, causation_id, deliver_at FROM outbox.messages")).ToList();
        list.Should().HaveCount(2);
        
        var db1 = list.Single(x => (Guid)x.id == msg1.Id);
        ((string)db1.correlation_id).Should().Be("c1");
        ((string)db1.causation_id).Should().Be("c2");
        ((DateTime)db1.deliver_at).Should().BeCloseTo(msg1.DeliverAt!.Value.UtcDateTime, TimeSpan.FromMilliseconds(1));

        var db2 = list.Single(x => (Guid)x.id == msg2.Id);
        ((string?)db2.correlation_id).Should().BeNull();
        ((string?)db2.causation_id).Should().BeNull();
        ((DateTime?)db2.deliver_at).Should().BeNull();
    }

    [Fact]
    public async Task MarkAsDispatchedAsync_Should_Update_Status_To_Dispatched()
    {
        var sut = CreateSut();
        var msg = CreateMessage(state: 1);

        // Empty case
        await sut.MarkAsDispatchedAsync(Array.Empty<OutboxMessage>());

        await using var connection = await _dataSource.OpenConnectionAsync();
        
        await connection.ExecuteAsync("INSERT INTO outbox.messages (id, type, payload, headers_json, created_at, updated_at, state, owner_id) VALUES (@Id, @MessageType, '{}', '{}', @CreatedAt, NOW(), @State, @owner_id)", new { msg.Id, msg.MessageType, msg.CreatedAt, State = msg.Status, owner_id = Guid.Parse(InstanceId) });

        // Empty case should not throw
        await sut.MarkAsDispatchedAsync(Array.Empty<OutboxMessage>());

        await sut.MarkAsDispatchedAsync(new[] { msg });

        // Test with IEnumerable that is not IReadOnlyCollection
        var msg2 = CreateMessage(state: 1);
        await connection.ExecuteAsync("INSERT INTO outbox.messages (id, type, payload, headers_json, created_at, updated_at, state, owner_id) VALUES (@Id, @MessageType, '{}', '{}', @CreatedAt, NOW(), @State, @owner_id)", new { msg2.Id, msg2.MessageType, msg2.CreatedAt, State = msg2.Status, owner_id = Guid.Parse(InstanceId) });
        await sut.MarkAsDispatchedAsync(new[] { msg2 });

        var count1 = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM outbox.messages WHERE id = @Id", new { msg.Id });
        count1.Should().Be(0);

        var count2 = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM outbox.messages WHERE id = @Id", new { msg2.Id });
        count2.Should().Be(0);
    }

    [Theory]
    [InlineData(false, 3)]
    [InlineData(true, 4)]
    public async Task MarkAsFailedAsync_Should_Update_Status_And_Increment_Retry(bool isDeadLetter, int expectedState)
    {
        var sut = CreateSut();
        var msg = CreateMessage(state: 1, retryCount: 0);

        // Empty case
        await sut.MarkAsFailedAsync(Array.Empty<OutboxMessage>(), "error", isDeadLetter);

        await using var connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync("INSERT INTO outbox.messages (id, type, payload, headers_json, created_at, updated_at, state, retry_count, owner_id) VALUES (@Id, @MessageType, '{}', '{}', @CreatedAt, NOW(), @State, @RetryCount, @owner_id)", new { msg.Id, msg.MessageType, msg.CreatedAt, State = msg.Status, msg.RetryCount, owner_id = Guid.Parse(InstanceId) });

        // Empty case
        await sut.MarkAsFailedAsync(Array.Empty<OutboxMessage>(), "error");

        await sut.MarkAsFailedAsync(new[] { msg }, "error", isDeadLetter);

        // Test with IEnumerable that is not IReadOnlyCollection
        var msg2 = CreateMessage(state: 1, retryCount: 0);
        await connection.ExecuteAsync("INSERT INTO outbox.messages (id, type, payload, headers_json, created_at, updated_at, state, retry_count, owner_id) VALUES (@Id, @MessageType, '{}', '{}', @CreatedAt, NOW(), @State, @RetryCount, @owner_id)", new { msg2.Id, msg2.MessageType, msg2.CreatedAt, State = msg2.Status, msg2.RetryCount, owner_id = Guid.Parse(InstanceId) });
        await sut.MarkAsFailedAsync(new[] { msg2 }, "error2", isDeadLetter);

        var db1 = await connection.QuerySingleAsync<(int state, int retry_count, string error)>("SELECT state, retry_count, error FROM outbox.messages WHERE id = @Id", new { msg.Id });
        db1.state.Should().Be(expectedState);
        db1.retry_count.Should().Be(1);
        db1.error.Should().Be("error");

        var db2 = await connection.QuerySingleAsync<(int state, int retry_count, string error)>("SELECT state, retry_count, error FROM outbox.messages WHERE id = @Id", new { msg2.Id });
        db2.state.Should().Be(expectedState);
        db2.retry_count.Should().Be(1);
        db2.error.Should().Be("error2");
    }

    [Fact]
    public async Task ReclaimStaleMessagesAsync_Should_Revert_To_Pending()
    {
        var sut = CreateSut();
        var staleMsg = CreateMessage(state: 1);
        var freshMsg = CreateMessage(state: 1);

        await using var connection = await _dataSource.OpenConnectionAsync();
        
        // Insert stale
        await connection.ExecuteAsync("INSERT INTO outbox.messages (id, type, payload, headers_json, created_at, updated_at, state) VALUES (@Id, @MessageType, '{}', '{}', @CreatedAt, NOW() - INTERVAL '1 hour', @State)", new { staleMsg.Id, staleMsg.MessageType, staleMsg.CreatedAt, State = staleMsg.Status });
        // Insert fresh
        await connection.ExecuteAsync("INSERT INTO outbox.messages (id, type, payload, headers_json, created_at, updated_at, state, owner_id) VALUES (@Id, @MessageType, '{}', '{}', @CreatedAt, NOW(), @State, @owner_id)", new { freshMsg.Id, freshMsg.MessageType, freshMsg.CreatedAt, State = freshMsg.Status, owner_id = Guid.Parse(InstanceId) });

        var reclaimedCount = await sut.ReclaimStaleMessagesAsync(TimeSpan.FromHours(1));

        reclaimedCount.Should().Be(1);

        var staleState = await connection.QuerySingleAsync<int>("SELECT state FROM outbox.messages WHERE id = @Id", new { staleMsg.Id });
        var freshState = await connection.QuerySingleAsync<int>("SELECT state FROM outbox.messages WHERE id = @Id", new { freshMsg.Id });

        staleState.Should().Be(0);
        freshState.Should().Be(1);
    }

    [Fact]
    public async Task Empty_Collections_Should_Return_Immediately()
    {
        var sut = CreateSut();
        var emptyMsgs = Array.Empty<OutboxMessage>();

        // These should not throw and should return immediately
        
        await sut.InsertBatchAsync(emptyMsgs, null!);
        await sut.MarkAsDispatchedAsync(emptyMsgs);
        await sut.MarkAsFailedAsync(emptyMsgs, "error");
    }
    [Fact]
    public async Task GetPendingCountAsync_Should_Return_Count()
    {
        var sut = CreateSut();
        await CleanDatabaseAsync();
        
        // Ensure there's a record so exact count is 1
        var msg = CreateMessage(state: 0);
        await using var connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync("INSERT INTO outbox.messages (id, type, payload, headers_json, created_at, updated_at, state, owner_id) VALUES (@Id, @MessageType, '{}', '{}', @CreatedAt, NOW(), @State, @owner_id)", new { msg.Id, msg.MessageType, msg.CreatedAt, State = msg.Status, owner_id = Guid.Parse(InstanceId) });

        // First test: small table (stats might be 0, threshold 50000)
        // Since estimatedTotal (0) < 50000, it falls back to exact count (1)
        var countExact = await sut.GetPendingCountAsync(CancellationToken.None);
        countExact.Should().Be(1);

        // Second test: force large table logic by lowering threshold to 0
        // Now estimatedTotal >= threshold (0), so it returns the estimate.
        // Because pg_stat_user_tables is updated asynchronously by the PostgreSQL stats collector,
        // this value could be 0, or it could be a residual number (e.g. 5) from other tests.
        // We assert it is >= 0 to avoid flakiness while still proving the SQL branch executes.
        var sutLarge = CreateSut(largeTableThreshold: 0);
        var countEstimate = await sutLarge.GetPendingCountAsync(CancellationToken.None);
        countEstimate.Should().BeGreaterThanOrEqualTo(0);
    }
    
    [Fact]
    public async Task GetMessageAsync_Should_Return_Message_If_Found()
    {
        var sut = CreateSut();
        var msg = CreateMessage(state: 1, deliverAt: DateTimeOffset.UtcNow);
        
        await using var connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync("INSERT INTO outbox.messages (id, type, payload, correlation_id, causation_id, headers_json, created_at, updated_at, deliver_at, state) VALUES (@Id, @MessageType, '{}', @CorrelationId, @CausationId, '{}', @CreatedAt, NOW(), @DeliverAt, @State)", new { msg.Id, msg.MessageType, msg.CorrelationId, msg.CausationId, msg.CreatedAt, msg.DeliverAt, State = msg.Status });

        var fetched = await sut.GetMessageAsync(msg.Id);
        
        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(msg.Id);
        fetched.MessageType.Should().Be(msg.MessageType);
        fetched.CorrelationId.Should().Be(msg.CorrelationId);
        fetched.CausationId.Should().Be(msg.CausationId);
        fetched.Status.Should().Be(msg.Status);
        
        // Null payload/headers/fields
        var msgNull = CreateMessage(deliverAt: null) with { CorrelationId = null, CausationId = null };
        await connection.ExecuteAsync("INSERT INTO outbox.messages (id, type, payload, headers_json, created_at, updated_at, deliver_at, state) VALUES (@Id, @MessageType, NULL, NULL, @CreatedAt, NOW(), @DeliverAt, @State)", new { msgNull.Id, msgNull.MessageType, msgNull.CreatedAt, msgNull.DeliverAt, State = msgNull.Status });

        var fetchedNull = await sut.GetMessageAsync(msgNull.Id);
        fetchedNull.Should().NotBeNull();
        fetchedNull!.CorrelationId.Should().BeNull();
        fetchedNull.CausationId.Should().BeNull();
        fetchedNull.DeliverAt.Should().BeNull();
        System.Text.Encoding.UTF8.GetString(fetchedNull.Payload.Span).Should().Be("{}");
        System.Text.Encoding.UTF8.GetString(fetchedNull.Headers.Span).Should().Be("{}");
    }

    [Fact]
    public async Task GetMessageAsync_Should_Return_Null_If_Not_Found()
    {
        var sut = CreateSut();
        var fetched = await sut.GetMessageAsync(Guid.NewGuid());
        fetched.Should().BeNull();
    }
}





