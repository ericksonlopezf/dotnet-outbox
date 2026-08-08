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

using EricksonLopez.Outbox.Storage.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Testcontainers.MsSql;
using Xunit;

namespace EricksonLopez.Outbox.Tests;

public class SqlServerContainerFixture : IAsyncLifetime
{
    public MsSqlContainer Container { get; } = new MsSqlBuilder().WithImage("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public Task InitializeAsync() => Container.StartAsync();
    public Task DisposeAsync() => Container.DisposeAsync().AsTask();
}

public class SqlServerOutboxRepositoryTests : IClassFixture<SqlServerContainerFixture>, IAsyncLifetime
{
    private readonly SqlServerContainerFixture _fixture;
    private readonly IFixture _autoFixture;
    private readonly EricksonLopez.Outbox.OutboxRuntimeOptions _options = new() { TableName = "outbox_messages" };
    public string InstanceId => _options.InstanceId;

    public SqlServerOutboxRepositoryTests(SqlServerContainerFixture fixture)
    {
        _fixture = fixture;
        _autoFixture = new Fixture().Customize(new AutoNSubstituteCustomization());
    }

    public async Task InitializeAsync()
    {
        using var connection = new SqlConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();

        const string schema = @"
            IF SCHEMA_ID('outbox') IS NULL
            BEGIN
                EXEC('CREATE SCHEMA [outbox]');
            END

            IF TYPE_ID('outbox.MessageKeysType') IS NULL
            BEGIN
                CREATE TYPE outbox.MessageKeysType AS TABLE (
                    [Id] UNIQUEIDENTIFIER NOT NULL,
                    [CreatedAt] DATETIMEOFFSET NOT NULL,
                    PRIMARY KEY ([Id], [CreatedAt])
                );
            END

            IF OBJECT_ID('outbox.outbox_messages', 'U') IS NULL
            BEGIN
                CREATE TABLE [outbox].[outbox_messages] (
                    id UNIQUEIDENTIFIER PRIMARY KEY,
                    type NVARCHAR(255) NOT NULL,
                    payload VARBINARY(MAX),
                    correlation_id NVARCHAR(255),
                    causation_id NVARCHAR(255),
                    headers_json VARBINARY(MAX),
                    created_at DATETIMEOFFSET NOT NULL,
                    updated_at DATETIMEOFFSET NOT NULL,
                    processed_at DATETIMEOFFSET,
                    deliver_at DATETIMEOFFSET,
                    state INT NOT NULL,
                    retry_count INT NOT NULL DEFAULT 0,
                    owner_id UNIQUEIDENTIFIER,
                    error NVARCHAR(MAX)
                );
            END
            ELSE
            BEGIN
                TRUNCATE TABLE [outbox].[outbox_messages];
            END


            IF OBJECT_ID('outbox.messages_dead_letters', 'U') IS NULL
            BEGIN
                CREATE TABLE [outbox].[messages_dead_letters] (
                    id UNIQUEIDENTIFIER PRIMARY KEY,
                    original_message_id UNIQUEIDENTIFIER NOT NULL,
                    type NVARCHAR(255) NOT NULL,
                    payload NVARCHAR(MAX),
                    correlation_id NVARCHAR(255),
                    causation_id NVARCHAR(255),
                    headers_json NVARCHAR(MAX),
                    created_at DATETIMEOFFSET NOT NULL,
                    dead_lettered_at DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
                    retry_count INT NOT NULL,
                    reason NVARCHAR(MAX),
                    last_error NVARCHAR(MAX)
                );
            END
            ELSE
            BEGIN
                TRUNCATE TABLE [outbox].[messages_dead_letters];
            END";
        
        await connection.ExecuteAsync(schema);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private SqlServerOutboxRepository CreateSut()
    {
        return new SqlServerOutboxRepository(() => new SqlConnection(_fixture.Container.GetConnectionString()), Microsoft.Extensions.Options.Options.Create(_options));
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
        var msg = CreateMessage();

        await using var connection = new SqlConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        
        await sut.InsertAsync(msg, new EricksonLopez.Outbox.Persistence.DbTransactionContext(tx));
        await tx.CommitAsync();

        var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM [outbox].[outbox_messages] WHERE id = @Id", new { msg.Id });
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

        await using var connection = new SqlConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        
        foreach(var m in new[] { pendingReady, pendingNotReady, dispatched, retryingReady })
        {
            var payloadBytes = m.Payload.ToArray();
            var headersBytes = m.Headers.ToArray();
            await connection.ExecuteAsync(
            "INSERT INTO [outbox].[outbox_messages] (id, type, correlation_id, causation_id, payload, headers_json, created_at, updated_at, deliver_at, state) VALUES (@Id, @MessageType, @CorrelationId, @CausationId, @PayloadBytes, @HeadersBytes, @CreatedAt, SYSDATETIMEOFFSET(), @DeliverAt, @State)", 
            new { m.Id, m.MessageType, m.CorrelationId, m.CausationId, PayloadBytes = payloadBytes, HeadersBytes = headersBytes, m.CreatedAt, m.DeliverAt, State = m.Status });
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
        var dbState = await connection.QuerySingleAsync<int>("SELECT state FROM [outbox].[outbox_messages] WHERE id = @Id", new { pendingReady.Id });
        dbState.Should().Be(1);

        // Test with NULL payload and headers to cover fallback logic
        var nullPayloadMsg = CreateMessage(state: 0, deliverAt: DateTimeOffset.UtcNow.AddMinutes(-1));
        await connection.ExecuteAsync("INSERT INTO [outbox].[outbox_messages] (id, type, payload, headers_json, created_at, updated_at, deliver_at, state) VALUES (@Id, @MessageType, NULL, NULL, @CreatedAt, SYSDATETIMEOFFSET(), @DeliverAt, @State)", new { nullPayloadMsg.Id, nullPayloadMsg.MessageType, nullPayloadMsg.CreatedAt, nullPayloadMsg.DeliverAt, State = nullPayloadMsg.Status });
        
        var fetchedNull = await sut.FetchPendingAsync(10);
        var fetchedNullMsg = fetchedNull.Single(x => x.Id == nullPayloadMsg.Id);
        System.Text.Encoding.UTF8.GetString(fetchedNullMsg.Payload.Span).Should().Be("{}");
        System.Text.Encoding.UTF8.GetString(fetchedNullMsg.Headers.Span).Should().Be("{}");
        
        // Test with invalid state to cover continue
        var invalidStateMsgId = Guid.NewGuid();
        await connection.ExecuteAsync("INSERT INTO [outbox].[outbox_messages] (id, type, payload, headers_json, created_at, updated_at, deliver_at, state) VALUES (@Id, 'test', NULL, NULL, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), NULL, 999)", new { Id = invalidStateMsgId });
        var fetchedInvalid = await sut.FetchPendingAsync(10);
        fetchedInvalid.Should().NotContain(x => x.Id == invalidStateMsgId);
    }

    [Fact]
    public async Task InsertBatchAsync_Should_Persist_Multiple_Messages()
    {
        var sut = CreateSut();
        var msg1 = CreateMessage(correlationId: "c1", causationId: "c2", deliverAt: DateTimeOffset.UtcNow.AddMinutes(5));
        var msg2 = CreateMessage(deliverAt: null) with { CorrelationId = null, CausationId = null };
        var messages = new[] { msg1, msg2 };

        await using var connection = new SqlConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        await using var tx = connection.BeginTransaction();
        
        await sut.InsertBatchAsync(messages, new EricksonLopez.Outbox.Persistence.DbTransactionContext(tx));
        await tx.CommitAsync();

        var list = (await connection.QueryAsync("SELECT id, correlation_id, causation_id, deliver_at FROM [outbox].[outbox_messages]")).ToList();
        list.Should().HaveCount(2);
        
        var db1 = list.Single(x => x.id == msg1.Id);
        ((string)db1.correlation_id).Should().Be("c1");
        ((string)db1.causation_id).Should().Be("c2");
        ((DateTimeOffset)db1.deliver_at).Should().BeCloseTo(msg1.DeliverAt!.Value, TimeSpan.FromMilliseconds(1));

        var db2 = list.Single(x => x.id == msg2.Id);
        ((string?)db2.correlation_id).Should().BeNull();
        ((string?)db2.causation_id).Should().BeNull();
        ((DateTimeOffset?)db2.deliver_at).Should().BeNull();
    }

    [Fact]
    public async Task MarkAsDispatchedAsync_Should_Update_Status_To_Dispatched()
    {
        var sut = CreateSut();
        var msg = CreateMessage(state: 1);

        // Empty case
        await sut.MarkAsDispatchedAsync(Array.Empty<OutboxMessage>());

        await using var connection = new SqlConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        await connection.ExecuteAsync("INSERT INTO [outbox].[outbox_messages] (id, type, payload, headers_json, created_at, updated_at, state, owner_id) VALUES (@Id, @MessageType, 0x7B7D, 0x7B7D, @CreatedAt, SYSDATETIMEOFFSET(), @State, @OwnerId)", new { msg.Id, msg.MessageType, msg.CreatedAt, State = msg.Status, OwnerId = Guid.Parse(InstanceId) });

        // Empty case should not throw
        await sut.MarkAsDispatchedAsync(Array.Empty<OutboxMessage>());

        await sut.MarkAsDispatchedAsync(new[] { msg });

        // Test with IEnumerable that is not IReadOnlyCollection
        var msg2 = CreateMessage(state: 1);
        await connection.ExecuteAsync("INSERT INTO [outbox].[outbox_messages] (id, type, payload, headers_json, created_at, updated_at, state, owner_id) VALUES (@Id, @MessageType, 0x7B7D, 0x7B7D, @CreatedAt, SYSDATETIMEOFFSET(), @State, @OwnerId)", new { msg2.Id, msg2.MessageType, msg2.CreatedAt, State = msg2.Status, OwnerId = Guid.Parse(InstanceId) });
        await sut.MarkAsDispatchedAsync(new[] { msg2 });

        var count1 = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM [outbox].[outbox_messages] WHERE id = @Id", new { msg.Id });
        count1.Should().Be(0);

        var count2 = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM [outbox].[outbox_messages] WHERE id = @Id", new { msg2.Id });
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

        await using var connection = new SqlConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        await connection.ExecuteAsync("INSERT INTO [outbox].[outbox_messages] (id, type, payload, headers_json, created_at, updated_at, state, retry_count, owner_id) VALUES (@Id, @MessageType, 0x7B7D, 0x7B7D, @CreatedAt, SYSDATETIMEOFFSET(), @State, @RetryCount, @OwnerId)", new { msg.Id, msg.MessageType, msg.CreatedAt, State = msg.Status, msg.RetryCount, OwnerId = Guid.Parse(InstanceId) });

        // Empty case
        await sut.MarkAsFailedAsync(Array.Empty<OutboxMessage>(), "error");

        await sut.MarkAsFailedAsync(new[] { msg }, "error", isDeadLetter);

        // Test with IEnumerable that is not IReadOnlyCollection
        var msg2 = CreateMessage(state: 1, retryCount: 0);
        await connection.ExecuteAsync("INSERT INTO [outbox].[outbox_messages] (id, type, payload, headers_json, created_at, updated_at, state, retry_count, owner_id) VALUES (@Id, @MessageType, 0x7B7D, 0x7B7D, @CreatedAt, SYSDATETIMEOFFSET(), @State, @RetryCount, @OwnerId)", new { msg2.Id, msg2.MessageType, msg2.CreatedAt, State = msg2.Status, msg2.RetryCount, OwnerId = Guid.Parse(InstanceId) });
        await sut.MarkAsFailedAsync(new[] { msg2 }, "error2", isDeadLetter);

        var db1 = await connection.QuerySingleAsync<(int state, int retry_count, string error)>("SELECT state, retry_count, error FROM [outbox].[outbox_messages] WHERE id = @Id", new { msg.Id });
        db1.state.Should().Be(expectedState);
        db1.retry_count.Should().Be(1);
        db1.error.Should().Be("error");

        var db2 = await connection.QuerySingleAsync<(int state, int retry_count, string error)>("SELECT state, retry_count, error FROM [outbox].[outbox_messages] WHERE id = @Id", new { msg2.Id });
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

        await using var connection = new SqlConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        
        // Insert stale
        await connection.ExecuteAsync("INSERT INTO [outbox].[outbox_messages] (id, type, payload, headers_json, created_at, updated_at, state, owner_id) VALUES (@Id, @MessageType, 0x7B7D, 0x7B7D, @CreatedAt, DATEADD(SECOND, -3600, SYSDATETIMEOFFSET()), @State, @OwnerId)", new { staleMsg.Id, staleMsg.MessageType, staleMsg.CreatedAt, State = staleMsg.Status, OwnerId = Guid.Parse(InstanceId) });
        // Insert fresh
        await connection.ExecuteAsync("INSERT INTO [outbox].[outbox_messages] (id, type, payload, headers_json, created_at, updated_at, state, owner_id) VALUES (@Id, @MessageType, 0x7B7D, 0x7B7D, @CreatedAt, SYSDATETIMEOFFSET(), @State, @OwnerId)", new { freshMsg.Id, freshMsg.MessageType, freshMsg.CreatedAt, State = freshMsg.Status, OwnerId = Guid.Parse(InstanceId) });

        var reclaimedCount = await sut.ReclaimStaleMessagesAsync(TimeSpan.FromHours(1));

        reclaimedCount.Should().Be(1);

        var staleState = await connection.QuerySingleAsync<int>("SELECT state FROM [outbox].[outbox_messages] WHERE id = @Id", new { staleMsg.Id });
        var freshState = await connection.QuerySingleAsync<int>("SELECT state FROM [outbox].[outbox_messages] WHERE id = @Id", new { freshMsg.Id });

        staleState.Should().Be(0);
        freshState.Should().Be(1);
    }

    [Fact]
    public async Task Empty_Collections_Should_Return_Immediately()
    {
        var sut = CreateSut();
        var emptyMsgs = Array.Empty<OutboxMessage>();

        // These should not throw and should return immediately
        
        await using var connection = new Microsoft.Data.SqlClient.SqlConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        await sut.InsertBatchAsync(emptyMsgs, null!);
        await sut.MarkAsDispatchedAsync(emptyMsgs);
        await sut.MarkAsFailedAsync(emptyMsgs, "error");
    }

    [Fact]
    public async Task SqlServerDeadLetterRepository_Should_Perform_CRUD()
    {
        var options = new Microsoft.Extensions.Options.OptionsMonitor<OutboxRuntimeOptions>(
            new Microsoft.Extensions.Options.OptionsFactory<OutboxRuntimeOptions>(
                Array.Empty<Microsoft.Extensions.Options.IConfigureOptions<OutboxRuntimeOptions>>(),
                Array.Empty<Microsoft.Extensions.Options.IPostConfigureOptions<OutboxRuntimeOptions>>()),
            Array.Empty<Microsoft.Extensions.Options.IOptionsChangeTokenSource<OutboxRuntimeOptions>>(),
            new Microsoft.Extensions.Options.OptionsCache<OutboxRuntimeOptions>());
        var repo = new SqlServerDeadLetterRepository(() => new SqlConnection(_fixture.Container.GetConnectionString()), options);
        
        var id = Guid.NewGuid();
        var msg = new DeadLetterMessage(
            id,
            Guid.NewGuid(),
            "test.msg",
            System.Text.Encoding.UTF8.GetBytes("payload"),
            "corr",
            "caus",
            System.Text.Encoding.UTF8.GetBytes("head"),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            5,
            "Failed Too Many Times",
            "NullReferenceException"
        );

        await repo.InsertAsync(msg);
        
        var items = await repo.GetAsync(10);
        items.Should().ContainSingle(x => x.Id == id);
        
        var item = items.Single(x => x.Id == id);
        item.Reason.Should().Be("Failed Too Many Times");

        await repo.DeleteAsync(id);
        
        var afterDelete = await repo.GetAsync(10);
        afterDelete.Should().NotContain(x => x.Id == id);

        await repo.InsertAsync(msg);
        await repo.PurgeAsync(DateTimeOffset.UtcNow.AddMinutes(1)); // Should delete
        var afterPurge = await repo.GetAsync(10);
        afterPurge.Should().BeEmpty();
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

        await using var connection = new SqlConnection(_fixture.Container.GetConnectionString());
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
    public void DataReader_Should_Return_Correct_Values()
    {
        var msg = CreateMessage(state: 1, deliverAt: DateTimeOffset.UtcNow);
        var records = new ReadOnlyMemory<OutboxMessage>(new[] { msg });
        
        // Use reflection to instantiate the private nested class
        var type = typeof(SqlServerOutboxRepository).GetNestedType("OutboxMessageDataReader", System.Reflection.BindingFlags.NonPublic);
        var reader = (System.Data.IDataReader)Activator.CreateInstance(type!, records)!;
        
        reader.RecordsAffected.Should().Be(-1);
        reader.GetOrdinal("unknown").Should().Be(-1);
        
        reader.Read().Should().BeTrue();
        
        // DBNull checks for properties we know we can make null
        var msgNullBase = CreateMessage(deliverAt: null);
        var msgNull = msgNullBase with { CorrelationId = null, CausationId = null };
        var recordsNull = new ReadOnlyMemory<OutboxMessage>(new[] { msgNull });
        var readerNull = (System.Data.IDataReader)Activator.CreateInstance(type!, recordsNull)!;
        readerNull.Read().Should().BeTrue();
        
        readerNull.IsDBNull(3).Should().BeTrue(); // correlation_id
        readerNull.IsDBNull(4).Should().BeTrue(); // causation_id
        readerNull.IsDBNull(9).Should().BeTrue(); // deliver_at
        
        // Full coverage of IDataReader methods
        reader.Depth.Should().Be(0);
        reader.IsClosed.Should().BeFalse();
        reader.GetSchemaTable().Should().BeNull();
        reader.NextResult().Should().BeFalse();
        reader.GetDataTypeName(0).Should().Be("");
        reader.GetFieldType(0).Should().Be<object>();
        reader.GetValues(Array.Empty<object>()).Should().Be(0);
        reader.Close();
        reader.Dispose();
        
        // Covering GetName switch
        reader.GetName(0).Should().Be("id");
        reader.GetName(1).Should().Be("type");
        reader.GetName(2).Should().Be("payload");
        reader.GetName(3).Should().Be("correlation_id");
        reader.GetName(4).Should().Be("causation_id");
        reader.GetName(5).Should().Be("headers_json");
        reader.GetName(6).Should().Be("state");
        reader.GetName(7).Should().Be("created_at");
        reader.GetName(8).Should().Be("updated_at");
        reader.GetName(9).Should().Be("deliver_at");
        Action actGetName = () => reader.GetName(10);
        actGetName.Should().Throw<ArgumentOutOfRangeException>();

        // Covering GetOrdinal switch
        reader.GetOrdinal("id").Should().Be(0);
        reader.GetOrdinal("type").Should().Be(1);
        reader.GetOrdinal("payload").Should().Be(2);
        reader.GetOrdinal("correlation_id").Should().Be(3);
        reader.GetOrdinal("causation_id").Should().Be(4);
        reader.GetOrdinal("headers_json").Should().Be(5);
        reader.GetOrdinal("state").Should().Be(6);
        reader.GetOrdinal("created_at").Should().Be(7);
        reader.GetOrdinal("updated_at").Should().Be(8);
        reader.GetOrdinal("deliver_at").Should().Be(9);
        
        // Covering GetValue default switch
        Action actGetValue = () => reader.GetValue(10);
        actGetValue.Should().Throw<ArgumentOutOfRangeException>();
        
        // Covering indexers
        reader[0].Should().Be(msg.Id);
        reader["id"].Should().Be(msg.Id);
        
        // Covering casting getters
        Action actBool = () => reader.GetBoolean(0); actBool.Should().Throw<InvalidCastException>();
        Action actByte = () => reader.GetByte(0); actByte.Should().Throw<InvalidCastException>();
        reader.GetBytes(0, 0, null, 0, 0).Should().Be(0);
        Action actChar = () => reader.GetChar(0); actChar.Should().Throw<InvalidCastException>();
        reader.GetChars(0, 0, null, 0, 0).Should().Be(0);
        Action actData = () => reader.GetData(0); actData.Should().Throw<NotSupportedException>();
        reader.GetDataTypeName(0).Should().Be("");
        
        Action actDateTime = () => reader.GetDateTime(0); actDateTime.Should().Throw<InvalidCastException>();
        Action actDecimal = () => reader.GetDecimal(0); actDecimal.Should().Throw<InvalidCastException>();
        Action actDouble = () => reader.GetDouble(0); actDouble.Should().Throw<InvalidCastException>();
        Action actFloat = () => reader.GetFloat(0); actFloat.Should().Throw<InvalidCastException>();
        
        reader.GetGuid(0).Should().Be(msg.Id);
        
        Action actInt16 = () => reader.GetInt16(0); actInt16.Should().Throw<InvalidCastException>();
        Action actInt32 = () => reader.GetInt32(0); actInt32.Should().Throw<InvalidCastException>();
        Action actInt64 = () => reader.GetInt64(0); actInt64.Should().Throw<InvalidCastException>();
        
        reader.GetString(1).Should().Be(msg.MessageType);
    }
}
















