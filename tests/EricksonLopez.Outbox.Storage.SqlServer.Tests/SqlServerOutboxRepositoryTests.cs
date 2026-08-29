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
using EricksonLopez.Outbox.Storage.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using NSubstitute;
using Testcontainers.MsSql;
using Xunit;

namespace EricksonLopez.Outbox.Storage.SqlServer.Tests;

[Collection("SqlServer")]
[Trait("Category", "Integration")]
public class SqlServerOutboxRepositoryTests : IAsyncLifetime
{
    private readonly SqlServerContainerFixture _fixture;
    private readonly IFixture _autoFixture;
    private readonly OutboxRuntimeOptions _options = new() { TableName = "outbox_messages" };
    public string InstanceId => _options.InstanceId;

    public SqlServerOutboxRepositoryTests(SqlServerContainerFixture fixture)
    {
        _fixture = fixture;
        _autoFixture = new Fixture().Customize(new AutoNSubstituteCustomization());
    }

    public async Task InitializeAsync()
    {
        await SqlServerTestDatabase.EnsureSchemaAsync(_fixture.Container.GetConnectionString(), _options.TableName);
    }

    public async Task DisposeAsync()
    {
        await using var connection = new SqlConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        await connection.ExecuteAsync($"TRUNCATE TABLE [outbox].[{_options.TableName}]");
    }

    private SqlServerOutboxRepository CreateSut(OutboxRuntimeOptions? customOptions = null)
    {
        return new SqlServerOutboxRepository(
            () => new SqlConnection(_fixture.Container.GetConnectionString()),
            Options.Create(customOptions ?? _options));
    }

    private OutboxMessage CreateMessage(
        int state = 0,
        DateTimeOffset? deliverAt = null,
        DateTimeOffset? createdAt = null,
        int retryCount = 0,
        string? correlationId = null,
        string? causationId = null)
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
        Action act = () => { _ = new SqlServerOutboxRepository(null!, Options.Create(_options)); };
        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionFactory");
    }

    [Theory]
    [InlineData("out-box")]
    [InlineData("dbo; DROP TABLE")]
    [InlineData("schema!")]
    [InlineData("")]
    public void Constructor_InvalidSchemaName_ThrowsArgumentException(string invalidSchema)
    {
        var options = new OutboxRuntimeOptions { SchemaName = invalidSchema, TableName = "messages" };
        Action act = () => { _ = new SqlServerOutboxRepository(() => new SqlConnection(), Options.Create(options)); };
        var ex = act.Should().Throw<ArgumentException>();
        ex.WithMessage("*Schema name contains invalid characters.*");
    }

    [Theory]
    [InlineData("outbox-messages")]
    [InlineData("tbl; DROP TABLE")]
    [InlineData("messages!")]
    [InlineData("")]
    public void Constructor_InvalidTableName_ThrowsArgumentException(string invalidTable)
    {
        var options = new OutboxRuntimeOptions { SchemaName = "dbo", TableName = invalidTable };
        Action act = () => { _ = new SqlServerOutboxRepository(() => new SqlConnection(), Options.Create(options)); };
        var ex = act.Should().Throw<ArgumentException>();
        ex.WithMessage("*Table name contains invalid characters.*");
    }

    [Fact]
    public async Task InsertAsync_Should_Persist_Message()
    {
        var sut = CreateSut();
        var msg = CreateMessage(correlationId: "corr-1", causationId: "caus-1", deliverAt: DateTimeOffset.UtcNow.AddMinutes(5));

        await using var connection = new SqlConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        
        await sut.InsertAsync(msg, new DbTransactionContext(tx));
        await tx.CommitAsync();

        var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM [outbox].[outbox_messages] WHERE id = @Id", new { msg.Id });
        count.Should().Be(1);
    }

    [Fact]
    public async Task InsertAsync_WithNullOptionalFields_PersistsDbNull()
    {
        var sut = CreateSut();
        var msg = CreateMessage() with { CorrelationId = null, CausationId = null, DeliverAt = null };

        await using var connection = new SqlConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        
        await sut.InsertAsync(msg, new DbTransactionContext(tx));
        await tx.CommitAsync();

        var dbMsg = await connection.QuerySingleAsync<(string? corr, string? caus, DateTimeOffset? del)>(
            "SELECT correlation_id, causation_id, deliver_at FROM [outbox].[outbox_messages] WHERE id = @Id", new { msg.Id });
        dbMsg.corr.Should().BeNull();
        dbMsg.caus.Should().BeNull();
        dbMsg.del.Should().BeNull();
    }

    [Fact]
    public async Task InsertAsync_InvalidTransactionConnection_ThrowsInvalidOperationException()
    {
        var sut = CreateSut();
        var msg = CreateMessage();
        var invalidTx = Substitute.For<IOutboxTransactionContext>();
        invalidTx.Connection.Returns(Substitute.For<IDbConnection>());

        Func<Task> act = async () => await sut.InsertAsync(msg, invalidTx);
        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.WithMessage("Transaction connection is not a SqlConnection.");
    }

    [Fact]
    public async Task FetchPendingAsync_Should_Return_Only_Pending_Messages_Ready_To_Deliver()
    {
        var sut = CreateSut();
        
        var pendingReady = CreateMessage(state: 0, deliverAt: DateTimeOffset.UtcNow.AddMinutes(-5), correlationId: "corr1", causationId: "caus1");
        var pendingFuture = CreateMessage(state: 0, deliverAt: DateTimeOffset.UtcNow.AddMinutes(5));
        var inFlight = CreateMessage(state: 1);
        var failedReady = CreateMessage(state: 3, deliverAt: DateTimeOffset.UtcNow.AddMinutes(-5));

        await using var connection = new SqlConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();
        
        await sut.InsertAsync(pendingReady, new DbTransactionContext(tx));
        await sut.InsertAsync(pendingFuture, new DbTransactionContext(tx));
        await sut.InsertAsync(inFlight, new DbTransactionContext(tx));
        await sut.InsertAsync(failedReady, new DbTransactionContext(tx));
        await tx.CommitAsync();

        await connection.ExecuteAsync("UPDATE [outbox].[outbox_messages] SET state = 1 WHERE id = @Id", new { inFlight.Id });
        await connection.ExecuteAsync("UPDATE [outbox].[outbox_messages] SET state = 3 WHERE id = @Id", new { failedReady.Id });

        var fetched = await sut.FetchPendingAsync(10);
        
        fetched.Should().HaveCount(2);
        fetched.Should().Contain(m => m.Id == pendingReady.Id);
        fetched.Should().Contain(m => m.Id == failedReady.Id);
        
        var first = fetched.First(m => m.Id == pendingReady.Id);
        first.Status.Should().Be(OutboxMessageStatus.InFlight);
        first.CorrelationId.Should().Be("corr1");
        first.CausationId.Should().Be("caus1");

        // Verify updated in DB to state 1
        var state = await connection.ExecuteScalarAsync<int>("SELECT state FROM [outbox].[outbox_messages] WHERE id = @Id", new { pendingReady.Id });
        state.Should().Be(1);
    }

    [Fact]
    public async Task FetchPendingAsync_WithNullAndNonNullNullableFields_MapsProperly()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using var connection = new SqlConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        
        var pBytes = System.Text.Encoding.UTF8.GetBytes("{\"custom\":\"unique_val_456\"}");
        var hBytes = System.Text.Encoding.UTF8.GetBytes("{\"custom\":\"unique_header_789\"}");
        await connection.ExecuteAsync(@"
            INSERT INTO [outbox].[outbox_messages] 
            (id, type, payload, correlation_id, causation_id, headers_json, state, created_at, updated_at, processed_at, deliver_at, error, retry_count)
            VALUES 
            (@Id1, 'type1', NULL, NULL, NULL, NULL, 0, @Now, @Now, NULL, NULL, NULL, 0),
            (@Id2, 'type2', @Payload, 'corr2', 'caus2', @Headers, 0, @Now, @Now, @Now, @Now, 'some error', 1)",
            new { Id1 = id1, Id2 = id2, Now = now, Payload = pBytes, Headers = hBytes });

        var sut = CreateSut();
        var fetched = await sut.FetchPendingAsync(10);

        var msg1 = fetched.FirstOrDefault(m => m.Id == id1);
        msg1.Should().NotBeNull();
        msg1!.ProcessedAt.Should().BeNull();
        msg1.DeliverAt.Should().BeNull();
        msg1.Error.Should().BeNull();
        System.Text.Encoding.UTF8.GetString(msg1.Payload.Span).Should().Be("{}");
        System.Text.Encoding.UTF8.GetString(msg1.Headers.Span).Should().Be("{}");

        var msg2 = fetched.FirstOrDefault(m => m.Id == id2);
        msg2.Should().NotBeNull();
        msg2!.ProcessedAt.Should().NotBeNull();
        msg2.DeliverAt.Should().NotBeNull();
        msg2.Error.Should().Be("some error");
        System.Text.Encoding.UTF8.GetString(msg2.Payload.Span).Should().Be("{\"custom\":\"unique_val_456\"}");
        System.Text.Encoding.UTF8.GetString(msg2.Headers.Span).Should().Be("{\"custom\":\"unique_header_789\"}");
    }

    [Fact]
    public async Task FetchPendingAsync_WithInvalidStateEnumInDatabase_SkipsInvalidRows()
    {
        var id = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;

        await using var connection = new SqlConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        
        // State 0 is selected by CTE, but we simulate a row that has an undefined state after CTE
        // (e.g. testing the Enum.IsDefined continue branch)
        await connection.ExecuteAsync(@"
            INSERT INTO [outbox].[outbox_messages] 
            (id, type, payload, headers_json, state, created_at, updated_at, retry_count)
            VALUES (@Id, 'type', 0x7B7D, 0x7B7D, 0, @Now, @Now, 0)",
            new { Id = id, Now = now });

        var sut = CreateSut();
        var fetched = await sut.FetchPendingAsync(10);
        fetched.Should().ContainSingle(m => m.Id == id);
    }

    [Fact]
    public async Task FetchPendingAsync_InvalidConnection_ThrowsInvalidOperationException()
    {
        var sut = new SqlServerOutboxRepository(() => Substitute.For<IDbConnection>(), Options.Create(_options));
        Func<Task> act = async () => await sut.FetchPendingAsync(10);
        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.WithMessage("Connection is not SqlConnection.");
    }

    [Fact]
    public async Task InsertBatchAsync_Should_Bulk_Insert_Messages()
    {
        var sut = CreateSut();
        var messages = new[]
        {
            CreateMessage(correlationId: "c1"),
            CreateMessage(causationId: "ca1"),
            CreateMessage(deliverAt: DateTimeOffset.UtcNow.AddHours(1))
        };

        await using var connection = new SqlConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        await using var tx = connection.BeginTransaction();

        await sut.InsertBatchAsync(messages, new DbTransactionContext(tx));
        await tx.CommitAsync();

        var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM [outbox].[outbox_messages]");
        count.Should().Be(3);
    }

    [Fact]
    public async Task InsertBatchAsync_EmptyRecords_ReturnsImmediately()
    {
        var sut = CreateSut();
        await sut.InsertBatchAsync(ReadOnlyMemory<OutboxMessage>.Empty, null!);
    }

    [Fact]
    public async Task InsertBatchAsync_InvalidTransactionConnection_ThrowsInvalidOperationException()
    {
        var sut = CreateSut();
        var messages = new[] { CreateMessage() };
        var invalidTx = Substitute.For<IOutboxTransactionContext>();
        invalidTx.Connection.Returns(Substitute.For<IDbConnection>());

        Func<Task> act = async () => await sut.InsertBatchAsync(messages, invalidTx);
        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.WithMessage("Transaction connection is not a SqlConnection.");
    }

    [Fact]
    public async Task MarkAsDispatchedAsync_Should_Delete_Messages()
    {
        var sut = CreateSut();
        var msg = CreateMessage(state: 1);

        await using var connection = new SqlConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        await connection.ExecuteAsync("INSERT INTO [outbox].[outbox_messages] (id, type, payload, headers_json, created_at, updated_at, state, owner_id) VALUES (@Id, @MessageType, 0x7B7D, 0x7B7D, @CreatedAt, SYSDATETIMEOFFSET(), @State, @OwnerId)", new { msg.Id, msg.MessageType, msg.CreatedAt, State = msg.Status, OwnerId = Guid.Parse(InstanceId) });

        await sut.MarkAsDispatchedAsync(new[] { msg });

        var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM [outbox].[outbox_messages] WHERE id = @Id", new { msg.Id });
        count.Should().Be(0);
    }

    [Fact]
    public async Task MarkAsDispatchedAsync_EmptyMessages_ReturnsImmediately()
    {
        var sut = CreateSut();
        await sut.MarkAsDispatchedAsync(Array.Empty<OutboxMessage>());
    }

    [Fact]
    public async Task MarkAsDispatchedAsync_InvalidConnection_ThrowsInvalidOperationException()
    {
        var sut = new SqlServerOutboxRepository(() => Substitute.For<IDbConnection>(), Options.Create(_options));
        Func<Task> act = async () => await sut.MarkAsDispatchedAsync(new[] { CreateMessage() });
        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.WithMessage("Connection is not SqlConnection.");
    }

    [Theory]
    [InlineData(false, 3)]
    [InlineData(true, 4)]
    public async Task MarkAsFailedAsync_Should_Update_Status_And_Increment_Retry(bool isDeadLetter, int expectedState)
    {
        var sut = CreateSut();
        var msg = CreateMessage(state: 1, retryCount: 0);

        await using var connection = new SqlConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        await connection.ExecuteAsync("INSERT INTO [outbox].[outbox_messages] (id, type, payload, headers_json, created_at, updated_at, state, retry_count, owner_id) VALUES (@Id, @MessageType, 0x7B7D, 0x7B7D, @CreatedAt, SYSDATETIMEOFFSET(), @State, @RetryCount, @OwnerId)", new { msg.Id, msg.MessageType, msg.CreatedAt, State = msg.Status, msg.RetryCount, OwnerId = Guid.Parse(InstanceId) });

        await sut.MarkAsFailedAsync(new[] { msg }, "error", isDeadLetter);

        var db = await connection.QuerySingleAsync<(int state, int retry_count, string error)>("SELECT state, retry_count, error FROM [outbox].[outbox_messages] WHERE id = @Id", new { msg.Id });
        db.state.Should().Be(expectedState);
        db.retry_count.Should().Be(1);
        db.error.Should().Be("error");
    }

    [Fact]
    public async Task MarkAsFailedAsync_EmptyMessages_ReturnsImmediately()
    {
        var sut = CreateSut();
        await sut.MarkAsFailedAsync(Array.Empty<OutboxMessage>(), "error");
    }

    [Fact]
    public async Task MarkAsFailedAsync_InvalidConnection_ThrowsInvalidOperationException()
    {
        var sut = new SqlServerOutboxRepository(() => Substitute.For<IDbConnection>(), Options.Create(_options));
        Func<Task> act = async () => await sut.MarkAsFailedAsync(new[] { CreateMessage() }, "error");
        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.WithMessage("Connection is not SqlConnection.");
    }

    [Fact]
    public async Task ReclaimStaleMessagesAsync_Should_Revert_To_Pending()
    {
        var sut = CreateSut();
        var staleMsg = CreateMessage(state: 1);
        var freshMsg = CreateMessage(state: 1);

        await using var connection = new SqlConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        
        await connection.ExecuteAsync("INSERT INTO [outbox].[outbox_messages] (id, type, payload, headers_json, created_at, updated_at, state, owner_id) VALUES (@Id, @MessageType, 0x7B7D, 0x7B7D, @CreatedAt, DATEADD(SECOND, -3600, SYSDATETIMEOFFSET()), @State, @OwnerId)", new { staleMsg.Id, staleMsg.MessageType, staleMsg.CreatedAt, State = staleMsg.Status, OwnerId = Guid.Parse(InstanceId) });
        await connection.ExecuteAsync("INSERT INTO [outbox].[outbox_messages] (id, type, payload, headers_json, created_at, updated_at, state, owner_id) VALUES (@Id, @MessageType, 0x7B7D, 0x7B7D, @CreatedAt, SYSDATETIMEOFFSET(), @State, @OwnerId)", new { freshMsg.Id, freshMsg.MessageType, freshMsg.CreatedAt, State = freshMsg.Status, OwnerId = Guid.Parse(InstanceId) });

        var reclaimedCount = await sut.ReclaimStaleMessagesAsync(TimeSpan.FromHours(1));
        reclaimedCount.Should().Be(1);

        var staleState = await connection.QuerySingleAsync<int>("SELECT state FROM [outbox].[outbox_messages] WHERE id = @Id", new { staleMsg.Id });
        var freshState = await connection.QuerySingleAsync<int>("SELECT state FROM [outbox].[outbox_messages] WHERE id = @Id", new { freshMsg.Id });

        staleState.Should().Be(0);
        freshState.Should().Be(1);
    }

    [Fact]
    public async Task ReclaimStaleMessagesAsync_InvalidConnection_ThrowsInvalidOperationException()
    {
        var sut = new SqlServerOutboxRepository(() => Substitute.For<IDbConnection>(), Options.Create(_options));
        Func<Task> act = async () => await sut.ReclaimStaleMessagesAsync(TimeSpan.FromSeconds(30));
        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.WithMessage("Connection is not SqlConnection.");
    }

    [Fact]
    public async Task GetPendingCountAsync_Should_Return_Count()
    {
        var sut = CreateSut();
        var count = await sut.GetPendingCountAsync(CancellationToken.None);
        count.Should().Be(0);

        var msg = CreateMessage(state: 0);
        await using var connection = new SqlConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        await connection.ExecuteAsync("INSERT INTO [outbox].[outbox_messages] (id, type, payload, headers_json, created_at, updated_at, state, retry_count) VALUES (@Id, @MessageType, 0x7B7D, 0x7B7D, @CreatedAt, SYSDATETIMEOFFSET(), 0, 0)", new { msg.Id, msg.MessageType, msg.CreatedAt });

        var countAfter = await sut.GetPendingCountAsync(CancellationToken.None);
        countAfter.Should().Be(1);
    }

    [Fact]
    public async Task GetPendingCountAsync_InvalidConnection_ThrowsInvalidOperationException()
    {
        var sut = new SqlServerOutboxRepository(() => Substitute.For<IDbConnection>(), Options.Create(_options));
        Func<Task> act = async () => await sut.GetPendingCountAsync();
        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.WithMessage("Connection is not SqlConnection.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task PurgeDispatchedMessagesAsync_WhenBatchSizeZeroOrNegative_ReturnsZeroImmediately(int batchSize)
    {
        var sut = new SqlServerOutboxRepository(() => Substitute.For<IDbConnection>(), Options.Create(_options));
        var result = await sut.PurgeDispatchedMessagesAsync(DateTimeOffset.UtcNow, batchSize: batchSize);
        result.Should().Be(0);
    }

    [Fact]
    public async Task PurgeDispatchedMessagesAsync_InvalidConnection_ThrowsInvalidOperationException()
    {
        var sut = new SqlServerOutboxRepository(() => Substitute.For<IDbConnection>(), Options.Create(_options));
        Func<Task> act = async () => await sut.PurgeDispatchedMessagesAsync(DateTimeOffset.UtcNow, batchSize: 100);
        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.WithMessage("Connection is not SqlConnection.");
    }

    [Fact]
    public async Task PurgeDispatchedMessagesAsync_WithClosedConnection_OpensAndPurges()
    {
        var sut = CreateSut();
        var msg = CreateMessage(state: 2);

        await using var connection = new SqlConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        await connection.ExecuteAsync("INSERT INTO [outbox].[outbox_messages] (id, type, payload, headers_json, created_at, updated_at, processed_at, state) VALUES (@Id, @MessageType, 0x7B7D, 0x7B7D, @CreatedAt, SYSDATETIMEOFFSET(), DATEADD(DAY, -2, SYSDATETIMEOFFSET()), 2)", new { msg.Id, msg.MessageType, msg.CreatedAt });

        var purged = await sut.PurgeDispatchedMessagesAsync(DateTimeOffset.UtcNow.AddDays(-1), batchSize: 100);
        purged.Should().Be(1);

        var remaining = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM [outbox].[outbox_messages] WHERE id = @Id", new { msg.Id });
        remaining.Should().Be(0);
    }

    [Fact]
    public async Task PurgeDispatchedMessagesAsync_WithAlreadyOpenConnection_PurgesWithoutReopening()
    {
        var msg = CreateMessage(state: 2);
        await using var openConn = new SqlConnection(_fixture.Container.GetConnectionString());
        await openConn.OpenAsync();
        await openConn.ExecuteAsync("INSERT INTO [outbox].[outbox_messages] (id, type, payload, headers_json, created_at, updated_at, processed_at, state) VALUES (@Id, @MessageType, 0x7B7D, 0x7B7D, @CreatedAt, SYSDATETIMEOFFSET(), DATEADD(DAY, -2, SYSDATETIMEOFFSET()), 2)", new { msg.Id, msg.MessageType, msg.CreatedAt });

        var sut = new SqlServerOutboxRepository(() => openConn, Options.Create(_options));
        var purged = await sut.PurgeDispatchedMessagesAsync(DateTimeOffset.UtcNow.AddDays(-1), batchSize: 100);
        purged.Should().Be(1);
    }

    [Fact]
    public async Task InsertBatchAsync_Should_Check_Cancellation_Token()
    {
        var sut = CreateSut();
        var messages = new[] { CreateMessage() };
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await using var connection = new SqlConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        await using var tx = connection.BeginTransaction();

        Func<Task> act = async () => await sut.InsertBatchAsync(messages, new DbTransactionContext(tx), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task FetchPendingAsync_Should_Check_Cancellation_Token()
    {
        var sut = CreateSut();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = async () => await sut.FetchPendingAsync(10, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task GetPendingCountAsync_Should_Check_Cancellation_Token()
    {
        var sut = CreateSut();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = async () => await sut.GetPendingCountAsync(cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void DataReader_Should_Return_Correct_Values()
    {
        var msg = CreateMessage(state: 1, deliverAt: DateTimeOffset.UtcNow);
        var records = new ReadOnlyMemory<OutboxMessage>(new[] { msg });
        
        var reader = new OutboxMessageDataReader(records);
        
        reader.FieldCount.Should().Be(10);
        reader.RecordsAffected.Should().Be(-1);
        reader.GetOrdinal("unknown").Should().Be(-1);
        
        reader.Read().Should().BeTrue();
        
        var msgNullBase = CreateMessage(deliverAt: null);
        var msgNull = msgNullBase with { CorrelationId = null, CausationId = null };
        var recordsNull = new ReadOnlyMemory<OutboxMessage>(new[] { msgNull });
        var readerNull = new OutboxMessageDataReader(recordsNull);
        readerNull.Read().Should().BeTrue();
        
        readerNull.IsDBNull(3).Should().BeTrue(); // correlation_id
        readerNull.IsDBNull(4).Should().BeTrue(); // causation_id
        readerNull.IsDBNull(9).Should().BeTrue(); // deliver_at
        
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
        
        Action actGetValue = () => reader.GetValue(10);
        actGetValue.Should().Throw<ArgumentOutOfRangeException>();

        reader.GetValue(0).Should().Be(msg.Id);
        reader.GetValue(1).Should().Be(msg.MessageType);
        ((byte[])reader.GetValue(2)).Should().Equal(msg.Payload.ToArray());
        reader.GetValue(3).Should().Be(msg.CorrelationId);
        reader.GetValue(4).Should().Be(msg.CausationId);
        ((byte[])reader.GetValue(5)).Should().Equal(msg.Headers.ToArray());
        reader.GetValue(6).Should().Be(msg.Status);
        reader.GetValue(7).Should().Be(msg.CreatedAt);
        reader.GetValue(8).Should().Be(msg.CreatedAt);
        reader.GetValue(9).Should().Be(msg.DeliverAt);
        
        reader[0].Should().Be(msg.Id);
        reader["id"].Should().Be(msg.Id);
        
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
