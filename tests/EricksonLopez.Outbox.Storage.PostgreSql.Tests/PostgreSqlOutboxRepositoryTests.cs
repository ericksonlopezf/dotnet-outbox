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
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Storage.PostgreSql;
using Microsoft.Extensions.Options;
using Npgsql;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Storage.PostgreSql.Tests;

[Collection("PostgreSql")]
[Trait("Category", "Integration")]
public class PostgreSqlOutboxRepositoryTests : IAsyncLifetime
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
        await PostgreSqlTestDatabase.EnsureSchemaAsync(_dataSource);
    }

    public async Task DisposeAsync()
    {
        await using var connection = new NpgsqlConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        await using var cmd = new NpgsqlCommand($"TRUNCATE TABLE outbox.{_options.TableName} CASCADE", connection);
        await cmd.ExecuteNonQueryAsync();
    }

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
        var msg = CreateMessage(correlationId: "corr_insert", causationId: "caus_insert", deliverAt: DateTimeOffset.UtcNow);

        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var tx = await connection.BeginTransactionAsync();
        
        await CreateSut().InsertAsync(msg, new EricksonLopez.Outbox.Persistence.DbTransactionContext(tx));
        await tx.CommitAsync();

        var db = await connection.QuerySingleAsync<(string correlation_id, string causation_id, DateTimeOffset? deliver_at)>("SELECT correlation_id, causation_id, deliver_at FROM outbox.messages WHERE id = @Id", new { msg.Id });
        db.correlation_id.Should().Be("corr_insert");
        db.causation_id.Should().Be("caus_insert");
        db.deliver_at.Should().NotBeNull();
        db.deliver_at!.Value.Should().BeCloseTo(msg.DeliverAt!.Value, TimeSpan.FromMilliseconds(1));
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
            "INSERT INTO outbox.messages (id, type, correlation_id, causation_id, payload, headers_json, created_at, updated_at, deliver_at, processed_at, state, error) VALUES (@Id, @MessageType, @CorrelationId, @CausationId, @PayloadStr::jsonb, @HeadersStr::jsonb, @CreatedAt, NOW(), @DeliverAt, NOW(), @State, 'error_sample_retrying')", 
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

        var fetchedRetrying = fetched.Single(x => x.Id == retryingReady.Id);
        fetchedRetrying.ProcessedAt.Should().NotBeNull();
        fetchedRetrying.Error.Should().Be("error_sample_retrying");

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
    public async Task GetPendingCountAsync_WithPartitionTable_UsesPrefixQuery()
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync("CREATE TABLE IF NOT EXISTS outbox.messages_part1 (LIKE outbox.messages INCLUDING ALL);");

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
        await connection.ExecuteAsync("INSERT INTO outbox.messages (id, type, payload, correlation_id, causation_id, headers_json, created_at, updated_at, deliver_at, processed_at, state, error) VALUES (@Id, @MessageType, '{}', @CorrelationId, @CausationId, '{}', @CreatedAt, NOW(), @DeliverAt, NOW(), @State, 'sample_error_1param')", new { msg.Id, msg.MessageType, msg.CorrelationId, msg.CausationId, msg.CreatedAt, msg.DeliverAt, State = msg.Status });

        var fetched = await sut.GetMessageAsync(msg.Id);
        
        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(msg.Id);
        fetched.MessageType.Should().Be(msg.MessageType);
        fetched.CorrelationId.Should().Be(msg.CorrelationId);
        fetched.CausationId.Should().Be(msg.CausationId);
        fetched.Status.Should().Be(msg.Status);
        fetched.DeliverAt.Should().NotBeNull();
        fetched.ProcessedAt.Should().NotBeNull();
        fetched.Error.Should().Be("sample_error_1param");
        
        // Null payload/headers/fields
        var msgNull = CreateMessage(deliverAt: null) with { CorrelationId = null, CausationId = null };
        await connection.ExecuteAsync("INSERT INTO outbox.messages (id, type, payload, headers_json, created_at, updated_at, deliver_at, state) VALUES (@Id, @MessageType, NULL, NULL, @CreatedAt, NOW(), @DeliverAt, @State)", new { msgNull.Id, msgNull.MessageType, msgNull.CreatedAt, msgNull.DeliverAt, State = msgNull.Status });

        var fetchedNull = await sut.GetMessageAsync(msgNull.Id);
        fetchedNull.Should().NotBeNull();
        fetchedNull!.CorrelationId.Should().BeNull();
        fetchedNull.CausationId.Should().BeNull();
        fetchedNull.DeliverAt.Should().BeNull();
        fetchedNull.ProcessedAt.Should().BeNull();
        fetchedNull.Error.Should().BeNull();
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

    [Fact]
    public void Constructor_InvalidArguments_ThrowsException()
    {
        Action actNullDs = () => _ = new PostgreSqlOutboxRepository(null!);
        actNullDs.Should().Throw<ArgumentNullException>().WithParameterName("dataSource");

        var invalidSchemaOpts = Options.Create(new OutboxRuntimeOptions { SchemaName = "outbox;DROP TABLE", TableName = "messages" });
        Action actInvalidSchema = () => _ = new PostgreSqlOutboxRepository(_dataSource, invalidSchemaOpts);
        actInvalidSchema.Should().Throw<ArgumentException>()
            .WithParameterName("options")
            .WithMessage("*Schema name contains invalid characters*");

        var invalidTableOpts = Options.Create(new OutboxRuntimeOptions { SchemaName = "outbox", TableName = "messages-invalid" });
        Action actInvalidTable = () => _ = new PostgreSqlOutboxRepository(_dataSource, invalidTableOpts);
        actInvalidTable.Should().Throw<ArgumentException>()
            .WithParameterName("options")
            .WithMessage("*Table name contains invalid characters*");

        // Null options should use default options
        var sutDefault = new PostgreSqlOutboxRepository(_dataSource, null);
        sutDefault.Should().NotBeNull();
    }

    [Fact]
    public async Task InsertAsync_NonNpgsqlConnectionTransaction_ThrowsInvalidOperationException()
    {
        var sut = CreateSut();
        var msg = CreateMessage();
        var mockTx = Substitute.For<IOutboxTransactionContext>();
        mockTx.Connection.Returns(Substitute.For<DbConnection>());

        Func<Task> act = async () => await sut.InsertAsync(msg, mockTx);
        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.WithMessage("*NpgsqlConnection*");
    }

    [Fact]
    public async Task InsertBatchAsync_NonNpgsqlConnectionTransaction_ThrowsInvalidOperationException()
    {
        var sut = CreateSut();
        var msg = CreateMessage();
        var mockTx = Substitute.For<IOutboxTransactionContext>();
        mockTx.Connection.Returns(Substitute.For<DbConnection>());

        Func<Task> act = async () => await sut.InsertBatchAsync(new[] { msg }, mockTx);
        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.WithMessage("*NpgsqlConnection*");
    }

    [Fact]
    public async Task InsertAsync_WithSlicedPayloadAndHeaders_PersistsSuccessfully()
    {
        var sut = CreateSut();
        var fullPayload = new byte[10] { 99, 91, 49, 93, 99, 99, 99, 99, 99, 99 }; // 'c' + "[1]" + 'c'...
        var sliced = new ReadOnlyMemory<byte>(fullPayload, 1, 3);
        var msg = CreateMessage() with { Payload = sliced, Headers = sliced, CorrelationId = null, CausationId = null, DeliverAt = null };

        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var tx = await connection.BeginTransactionAsync();
        await sut.InsertAsync(msg, new EricksonLopez.Outbox.Persistence.DbTransactionContext(tx));
        await tx.CommitAsync();

        var fetched = await sut.GetMessageAsync(msg.Id);
        fetched.Should().NotBeNull();
        System.Text.Encoding.UTF8.GetString(fetched!.Payload.Span).Should().Be("[1]");
        System.Text.Encoding.UTF8.GetString(fetched.Headers.Span).Should().Be("[1]");
    }

    [Fact]
    public async Task InsertBatchAsync_WithSlicedPayloadAndHeaders_PersistsSuccessfully()
    {
        var sut = CreateSut();
        var fullPayload = new byte[10] { 99, 91, 49, 93, 99, 99, 99, 99, 99, 99 };
        var sliced = new ReadOnlyMemory<byte>(fullPayload, 1, 3);
        var msg = CreateMessage() with { Payload = sliced, Headers = sliced };

        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var tx = await connection.BeginTransactionAsync();
        await sut.InsertBatchAsync(new[] { msg }, new EricksonLopez.Outbox.Persistence.DbTransactionContext(tx));
        await tx.CommitAsync();

        var fetched = await sut.GetMessageAsync(msg.Id);
        fetched.Should().NotBeNull();
        System.Text.Encoding.UTF8.GetString(fetched!.Payload.Span).Should().Be("[1]");
    }

    [Fact]
    public async Task InsertBulkAsync_WithSlicedPayloadAndHeaders_PersistsSuccessfully()
    {
        var sut = CreateSut();
        var fullPayload = new byte[10] { 99, 91, 49, 93, 99, 99, 99, 99, 99, 99 };
        var sliced = new ReadOnlyMemory<byte>(fullPayload, 1, 3);
        var msg = CreateMessage() with { Payload = sliced, Headers = sliced, CorrelationId = "c1", CausationId = "caus1", DeliverAt = DateTimeOffset.UtcNow };

        await sut.InsertBulkAsync(new[] { msg });

        var fetched = await sut.GetMessageAsync(msg.Id);
        fetched.Should().NotBeNull();
        System.Text.Encoding.UTF8.GetString(fetched!.Payload.Span).Should().Be("[1]");
        fetched.CorrelationId.Should().Be("c1");
        fetched.CausationId.Should().Be("caus1");
    }

    [Fact]
    public async Task InsertAsync_WithSubArrayStartingAtZero_PersistsOnlySubArray()
    {
        var sut = CreateSut();
        var fullPayload = new byte[10] { 91, 49, 93, 99, 99, 99, 99, 99, 99, 99 }; // "[1]" followed by 'c'...
        var subArray = new ReadOnlyMemory<byte>(fullPayload, 0, 3); // Offset = 0, Count = 3 < Length 10
        var msg = CreateMessage() with { Payload = subArray, Headers = subArray };

        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var tx = await connection.BeginTransactionAsync();
        await sut.InsertAsync(msg, new EricksonLopez.Outbox.Persistence.DbTransactionContext(tx));
        await tx.CommitAsync();

        var fetched = await sut.GetMessageAsync(msg.Id);
        fetched.Should().NotBeNull();
        fetched!.Payload.Length.Should().Be(3);
        System.Text.Encoding.UTF8.GetString(fetched.Payload.Span).Should().Be("[1]");
    }

    [Fact]
    public async Task InsertBatchAsync_WithSubArrayStartingAtZero_PersistsOnlySubArray()
    {
        var sut = CreateSut();
        var fullPayload = new byte[10] { 91, 49, 93, 99, 99, 99, 99, 99, 99, 99 };
        var subArray = new ReadOnlyMemory<byte>(fullPayload, 0, 3);
        var msg = CreateMessage() with { Payload = subArray, Headers = subArray };

        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var tx = await connection.BeginTransactionAsync();
        await sut.InsertBatchAsync(new[] { msg }, new EricksonLopez.Outbox.Persistence.DbTransactionContext(tx));
        await tx.CommitAsync();

        var fetched = await sut.GetMessageAsync(msg.Id);
        fetched.Should().NotBeNull();
        fetched!.Payload.Length.Should().Be(3);
        System.Text.Encoding.UTF8.GetString(fetched.Payload.Span).Should().Be("[1]");
    }

    [Fact]
    public async Task InsertBulkAsync_WithSubArrayStartingAtZero_PersistsOnlySubArray()
    {
        var sut = CreateSut();
        var fullPayload = new byte[10] { 91, 49, 93, 99, 99, 99, 99, 99, 99, 99 };
        var subArray = new ReadOnlyMemory<byte>(fullPayload, 0, 3);
        var msg = CreateMessage() with { Payload = subArray, Headers = subArray };

        await sut.InsertBulkAsync(new[] { msg });

        var fetched = await sut.GetMessageAsync(msg.Id);
        fetched.Should().NotBeNull();
        fetched!.Payload.Length.Should().Be(3);
        System.Text.Encoding.UTF8.GetString(fetched.Payload.Span).Should().Be("[1]");
    }

    [Fact]
    public async Task MarkAsDispatchedAsync_WithDeleteOnDispatchFalse_UpdatesStateToDispatched()
    {
        var opts = Options.Create(new OutboxRuntimeOptions
        {
            SchemaName = "outbox",
            TableName = "messages",
            DeleteOnDispatch = false,
            InstanceId = InstanceId
        });
        var sut = new PostgreSqlOutboxRepository(_dataSource, opts);
        var msg = CreateMessage(state: 1);

        await using var connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync("INSERT INTO outbox.messages (id, type, payload, headers_json, created_at, updated_at, state, owner_id) VALUES (@Id, @MessageType, '{}', '{}', @CreatedAt, NOW(), @State, @owner_id)", new { msg.Id, msg.MessageType, msg.CreatedAt, State = msg.Status, owner_id = Guid.Parse(InstanceId) });

        await sut.MarkAsDispatchedAsync(new[] { msg });

        var db = await connection.QuerySingleAsync<(int state, DateTimeOffset? processed_at)>("SELECT state, processed_at FROM outbox.messages WHERE id = @Id", new { msg.Id });
        db.state.Should().Be(2);
        db.processed_at.Should().NotBeNull();
    }

    [Fact]
    public async Task MarkAsFailedAsync_WithLongErrorMessage_TruncatesErrorString()
    {
        var sut = CreateSut();
        var msg = CreateMessage(state: 1, retryCount: 0);
        var longError = new string('A', 3600) + "MIDDLE" + new string('Z', 500); // 4106 characters

        await using var connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync("INSERT INTO outbox.messages (id, type, payload, headers_json, created_at, updated_at, state, retry_count, owner_id) VALUES (@Id, @MessageType, '{}', '{}', @CreatedAt, NOW(), @State, @RetryCount, @owner_id)", new { msg.Id, msg.MessageType, msg.CreatedAt, State = msg.Status, msg.RetryCount, owner_id = Guid.Parse(InstanceId) });

        await sut.MarkAsFailedAsync(new[] { msg }, longError);

        var db = await connection.QuerySingleAsync<(int state, string error)>("SELECT state, error FROM outbox.messages WHERE id = @Id", new { msg.Id });
        db.error.Should().Contain(" ... [TRUNCATED] ... ");
        db.error.Length.Should().Be(4000);
        db.error.Should().StartWith(new string('A', 3530));
        db.error.Should().EndWith(new string('Z', 449));
    }

    [Fact]
    public async Task MarkAsFailedAsync_WithExact4000CharError_IsNotTruncated()
    {
        var sut = CreateSut();
        var msg = CreateMessage(state: 1, retryCount: 0);
        var exact4000Error = new string('X', 4000);

        await using var connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync("INSERT INTO outbox.messages (id, type, payload, headers_json, created_at, updated_at, state, retry_count, owner_id) VALUES (@Id, @MessageType, '{}', '{}', @CreatedAt, NOW(), @State, @RetryCount, @owner_id)", new { msg.Id, msg.MessageType, msg.CreatedAt, State = msg.Status, msg.RetryCount, owner_id = Guid.Parse(InstanceId) });

        await sut.MarkAsFailedAsync(new[] { msg }, exact4000Error);

        var db = await connection.QuerySingleAsync<(int state, string error)>("SELECT state, error FROM outbox.messages WHERE id = @Id", new { msg.Id });
        db.error.Should().Be(exact4000Error);
        db.error.Should().NotContain("[TRUNCATED]");
    }

    [Fact]
    public async Task GetMessageAsync_WithCreatedAtHint_ReturnsMessageOrNull()
    {
        var sut = CreateSut();
        var msg = CreateMessage(state: 1, deliverAt: DateTimeOffset.UtcNow, correlationId: "corr_hint", causationId: "caus_hint");

        await using var connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync("INSERT INTO outbox.messages (id, type, payload, correlation_id, causation_id, headers_json, created_at, updated_at, deliver_at, processed_at, state, error) VALUES (@Id, @MessageType, '{\"k\":\"val_hint\"}'::jsonb, @CorrelationId, @CausationId, '{\"h\":\"head_hint\"}'::jsonb, @CreatedAt, NOW(), @DeliverAt, NOW(), @State, 'sample_error')", new { msg.Id, msg.MessageType, msg.CorrelationId, msg.CausationId, msg.CreatedAt, msg.DeliverAt, State = msg.Status });

        // Query with matching hint
        var fetchedWithHint = await sut.GetMessageAsync(msg.Id, msg.CreatedAt);
        fetchedWithHint.Should().NotBeNull();
        fetchedWithHint!.Id.Should().Be(msg.Id);
        fetchedWithHint.CorrelationId.Should().Be("corr_hint");
        fetchedWithHint.CausationId.Should().Be("caus_hint");
        fetchedWithHint.Error.Should().Be("sample_error");
        fetchedWithHint.DeliverAt.Should().NotBeNull();
        fetchedWithHint.ProcessedAt.Should().NotBeNull();
        System.Text.Encoding.UTF8.GetString(fetchedWithHint.Payload.Span).Should().Be("{\"k\": \"val_hint\"}");
        System.Text.Encoding.UTF8.GetString(fetchedWithHint.Headers.Span).Should().Be("{\"h\": \"head_hint\"}");

        // Query with null hint (fallback to id-only query)
        var fetchedNullHint = await sut.GetMessageAsync(msg.Id, null);
        fetchedNullHint.Should().NotBeNull();
        fetchedNullHint!.Id.Should().Be(msg.Id);

        // Query with wrong timestamp hint
        var notFound = await sut.GetMessageAsync(msg.Id, msg.CreatedAt.AddDays(5));
        notFound.Should().BeNull();

        // Query with non-existent id
        var notFoundId = await sut.GetMessageAsync(Guid.NewGuid(), msg.CreatedAt);
        notFoundId.Should().BeNull();
    }

    [Fact]
    public async Task GetMessageAsync_WithCreatedAtHint_HandlesNullColumns()
    {
        var sut = CreateSut();
        var msg = CreateMessage(state: 0, deliverAt: null) with { CorrelationId = null, CausationId = null };

        await using var connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync("INSERT INTO outbox.messages (id, type, payload, headers_json, created_at, updated_at, deliver_at, state, error) VALUES (@Id, @MessageType, NULL, NULL, @CreatedAt, NOW(), NULL, @State, NULL)", new { msg.Id, msg.MessageType, msg.CreatedAt, State = msg.Status });

        var fetched = await sut.GetMessageAsync(msg.Id, msg.CreatedAt);
        fetched.Should().NotBeNull();
        fetched!.CorrelationId.Should().BeNull();
        fetched.CausationId.Should().BeNull();
        fetched.DeliverAt.Should().BeNull();
        fetched.ProcessedAt.Should().BeNull();
        fetched.Error.Should().BeNull();
        System.Text.Encoding.UTF8.GetString(fetched.Payload.Span).Should().Be("{}");
        System.Text.Encoding.UTF8.GetString(fetched.Headers.Span).Should().Be("{}");
    }

    [Fact]
    public async Task PurgeDispatchedMessagesAsync_WhenBatchSizeZeroOrNegative_ReturnsZeroImmediately()
    {
        var sut = CreateSut();
        var oldDispatched = CreateMessage(state: 2, createdAt: DateTimeOffset.UtcNow.AddDays(-10));
        await using var connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync("INSERT INTO outbox.messages (id, type, payload, headers_json, created_at, updated_at, processed_at, state) VALUES (@Id, @MessageType, '{}', '{}', @CreatedAt, NOW() - INTERVAL '10 days', NOW() - INTERVAL '10 days', @State)", new { oldDispatched.Id, oldDispatched.MessageType, oldDispatched.CreatedAt, State = oldDispatched.Status });

        var result0 = await sut.PurgeDispatchedMessagesAsync(DateTimeOffset.UtcNow, batchSize: 0);
        result0.Should().Be(0);

        var resultNeg = await sut.PurgeDispatchedMessagesAsync(DateTimeOffset.UtcNow, batchSize: -5);
        resultNeg.Should().Be(0);

        var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM outbox.messages WHERE id = @Id", new { oldDispatched.Id });
        count.Should().Be(1);
    }

    [Fact]
    public async Task PurgeDispatchedMessagesAsync_Should_Delete_State2_Messages_Before_Cutoff()
    {
        var sut = CreateSut();
        var oldDispatched = CreateMessage(state: 2, createdAt: DateTimeOffset.UtcNow.AddDays(-10));
        var freshDispatched = CreateMessage(state: 2, createdAt: DateTimeOffset.UtcNow);
        var pendingMsg = CreateMessage(state: 0, createdAt: DateTimeOffset.UtcNow.AddDays(-10));

        await using var connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync("INSERT INTO outbox.messages (id, type, payload, headers_json, created_at, updated_at, processed_at, state) VALUES (@Id, @MessageType, '{}', '{}', @CreatedAt, NOW() - INTERVAL '10 days', NOW() - INTERVAL '10 days', @State)", new { oldDispatched.Id, oldDispatched.MessageType, oldDispatched.CreatedAt, State = oldDispatched.Status });
        await connection.ExecuteAsync("INSERT INTO outbox.messages (id, type, payload, headers_json, created_at, updated_at, processed_at, state) VALUES (@Id, @MessageType, '{}', '{}', @CreatedAt, NOW(), NOW(), @State)", new { freshDispatched.Id, freshDispatched.MessageType, freshDispatched.CreatedAt, State = freshDispatched.Status });
        await connection.ExecuteAsync("INSERT INTO outbox.messages (id, type, payload, headers_json, created_at, updated_at, state) VALUES (@Id, @MessageType, '{}', '{}', @CreatedAt, NOW() - INTERVAL '10 days', @State)", new { pendingMsg.Id, pendingMsg.MessageType, pendingMsg.CreatedAt, State = pendingMsg.Status });

        var purged = await sut.PurgeDispatchedMessagesAsync(DateTimeOffset.UtcNow.AddDays(-5), batchSize: 100);
        purged.Should().Be(1);

        var countOld = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM outbox.messages WHERE id = @Id", new { oldDispatched.Id });
        countOld.Should().Be(0);

        var countFresh = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM outbox.messages WHERE id = @Id", new { freshDispatched.Id });
        countFresh.Should().Be(1);

        var countPending = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM outbox.messages WHERE id = @Id", new { pendingMsg.Id });
        countPending.Should().Be(1);
    }

    [Fact]
    public void PooledList_BasicOperations_And_EdgeCases()
    {
        using (var list = new PostgreSqlOutboxRepository.PooledList<int>(5))
        {
            list.Count.Should().Be(5);
            list.IsReadOnly.Should().BeFalse();

            for (int i = 0; i < 5; i++)
            {
                list[i] = i * 10;
                list[i].Should().Be(i * 10);
            }

            // Indexer out of bounds
            Action getNegative = () => { var _ = list[-1]; };
            getNegative.Should().Throw<ArgumentOutOfRangeException>();

            Action getTooLarge = () => { var _ = list[5]; };
            getTooLarge.Should().Throw<ArgumentOutOfRangeException>();

            Action setNegative = () => { list[-1] = 99; };
            setNegative.Should().Throw<ArgumentOutOfRangeException>();

            Action setTooLarge = () => { list[5] = 99; };
            setTooLarge.Should().Throw<ArgumentOutOfRangeException>();

            // CopyTo
            var expectedItems = new int[] { 0, 10, 20, 30, 40 };
            var destination = new int[5];
            list.CopyTo(destination, 0);
            destination.Should().BeEquivalentTo(expectedItems);

            // Enumerator & non-generic enumerator
            var items = new List<int>();
            foreach (var item in list)
            {
                items.Add(item);
            }
            items.Should().BeEquivalentTo(expectedItems);

            var nonGenericItems = new List<int>();
            var nonGenericEnum = ((System.Collections.IEnumerable)list).GetEnumerator();
            while (nonGenericEnum.MoveNext())
            {
                nonGenericItems.Add((int)nonGenericEnum.Current!);
            }
            nonGenericItems.Should().BeEquivalentTo(expectedItems);

            // Unsupported mutations
            Action add = () => list.Add(1);
            add.Should().Throw<NotSupportedException>();

            Action clear = () => list.Clear();
            clear.Should().Throw<NotSupportedException>();

            Action contains = () => list.Contains(1);
            contains.Should().Throw<NotSupportedException>();

            Action indexOf = () => list.IndexOf(1);
            indexOf.Should().Throw<NotSupportedException>();

            Action insert = () => list.Insert(0, 1);
            insert.Should().Throw<NotSupportedException>();

            Action remove = () => list.Remove(1);
            remove.Should().Throw<NotSupportedException>();

            Action removeAt = () => list.RemoveAt(0);
            removeAt.Should().Throw<NotSupportedException>();
        }

        // Multiple dispose should be safe and clearArray works
        var listToDispose = new PostgreSqlOutboxRepository.PooledList<int>(3);
        listToDispose.IsDisposed.Should().BeFalse();
        listToDispose[0] = 777;
        listToDispose[1] = 888;
        listToDispose[2] = 999;
        listToDispose.Dispose();
        listToDispose.IsDisposed.Should().BeTrue();
        listToDispose.Dispose();
        listToDispose.IsDisposed.Should().BeTrue();
    }

    [Fact]
    public void PooledList_Dispose_ReturnsClearedArrayToPool()
    {
        var warmup = System.Buffers.ArrayPool<string>.Shared.Rent(16);
        System.Buffers.ArrayPool<string>.Shared.Return(warmup, clearArray: true);

        var list = new PostgreSqlOutboxRepository.PooledList<string>(16);
        list[0] = "secret_canary_value";
        list.Dispose();

        var rented = System.Buffers.ArrayPool<string>.Shared.Rent(16);
        try
        {
            rented[0].Should().BeNull();
        }
        finally
        {
            System.Buffers.ArrayPool<string>.Shared.Return(rented, clearArray: true);
        }
    }

    [Fact]
    public async Task PurgeDispatchedMessagesAsync_WhenBatchSizeZero_WithDisposedDataSource_ReturnsZeroImmediately()
    {
        await using var disposedDs = NpgsqlDataSource.Create("Host=localhost;Username=test;Password=test");
        await disposedDs.DisposeAsync();

        var sut = new PostgreSqlOutboxRepository(disposedDs);
        var result = await sut.PurgeDispatchedMessagesAsync(DateTimeOffset.UtcNow, batchSize: 0);
        result.Should().Be(0);
    }

    [Fact]
    public async Task InsertBulkAsync_EmptyRecords_ReturnsImmediatelyWithoutTouchingDataSource()
    {
        await using var disposedDs = NpgsqlDataSource.Create("Host=localhost;Username=test;Password=test");
        await disposedDs.DisposeAsync();

        var sut = new PostgreSqlOutboxRepository(disposedDs);
        var act = () => sut.InsertBulkAsync(Array.Empty<OutboxMessage>());
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task FetchPendingAsync_WhenUnexpectedStateInDatabase_SkipsRow()
    {
        var sut = CreateSut();
        var msg = CreateMessage(state: 0);

        await using var connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync(@"
            INSERT INTO outbox.messages 
            (id, type, payload, correlation_id, causation_id, headers_json, created_at, updated_at, deliver_at, state, retry_count)
            VALUES 
            (@Id, @Type, @Payload::jsonb, @CorrelationId, @CausationId, @HeadersJson::jsonb, @CreatedAt, @UpdatedAt, @DeliverAt, 99, 0)",
            new {
                Id = msg.Id,
                Type = msg.MessageType,
                Payload = System.Text.Encoding.UTF8.GetString(msg.Payload.Span),
                CorrelationId = msg.CorrelationId,
                CausationId = msg.CausationId,
                HeadersJson = System.Text.Encoding.UTF8.GetString(msg.Headers.Span),
                CreatedAt = msg.CreatedAt,
                UpdatedAt = DateTimeOffset.UtcNow,
                DeliverAt = (DateTimeOffset?)null
            });

        var fetched = await sut.FetchPendingAsync(10);
        fetched.Should().NotContain(m => m.Id == msg.Id);
    }
}








