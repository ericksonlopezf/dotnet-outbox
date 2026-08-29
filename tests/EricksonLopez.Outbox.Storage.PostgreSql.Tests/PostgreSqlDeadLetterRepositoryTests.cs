// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
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
public class PostgreSqlDeadLetterRepositoryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainerFixture _fixture;
    private readonly IFixture _autoFixture;
    protected NpgsqlDataSource _dataSource => _fixture.DataSource;

    public PostgreSqlDeadLetterRepositoryTests(PostgreSqlContainerFixture fixture)
    {
        _fixture = fixture;
        _autoFixture = new Fixture().Customize(new AutoNSubstituteCustomization());
    }

    public async Task InitializeAsync()
    {
        await PostgreSqlTestDatabase.EnsureSchemaAsync(_dataSource);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private PostgreSqlDeadLetterRepository CreateSut()
    {
        var options = new Microsoft.Extensions.Options.OptionsMonitor<OutboxRuntimeOptions>(
            new Microsoft.Extensions.Options.OptionsFactory<OutboxRuntimeOptions>(
                Array.Empty<Microsoft.Extensions.Options.IConfigureOptions<OutboxRuntimeOptions>>(),
                Array.Empty<Microsoft.Extensions.Options.IPostConfigureOptions<OutboxRuntimeOptions>>()),
            Array.Empty<Microsoft.Extensions.Options.IOptionsChangeTokenSource<OutboxRuntimeOptions>>(),
            new Microsoft.Extensions.Options.OptionsCache<OutboxRuntimeOptions>());
        return new PostgreSqlDeadLetterRepository(_dataSource, options);
    }

    private DeadLetterMessage CreateDeadLetterMessage()
    {
        var msg = _autoFixture.Create<DeadLetterMessage>();
        return new DeadLetterMessage(
            msg.Id,
            msg.OriginalMessageId,
            msg.MessageType,
            System.Text.Encoding.UTF8.GetBytes("{}"),
            msg.CorrelationId,
            msg.CausationId,
            System.Text.Encoding.UTF8.GetBytes("{}"),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            msg.RetryCount,
            msg.Reason,
            msg.LastError
        );
    }

    [Fact]
    public async Task InsertAsync_Should_Persist_Message_Without_Transaction()
    {
        var sut = CreateSut();
        var msg = CreateDeadLetterMessage();

        await sut.InsertAsync(msg);

        await using var connection = await _dataSource.OpenConnectionAsync();
        var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM outbox.messages_dead_letters WHERE id = @Id", new { msg.Id });
        count.Should().Be(1);
        
        // Assert payload/headers were correctly translated to strings for JSONB
        var textPayload = await connection.ExecuteScalarAsync<string>("SELECT payload::text FROM outbox.messages_dead_letters WHERE id = @Id", new { msg.Id });
        textPayload.Should().Be("{}"); // JSONB text representation adds quotes if it's a string, wait, {} is an object, so it will be just "{}"
        
        // Wait, if it's JSONB, the text is "{}"
        var headersText = await connection.ExecuteScalarAsync<string>("SELECT headers_json::text FROM outbox.messages_dead_letters WHERE id = @Id", new { msg.Id });
        headersText.Should().Be("{}");
    }

    [Fact]
    public async Task InsertAsync_Should_Persist_Message_With_Transaction()
    {
        var sut = CreateSut();
        var msg = CreateDeadLetterMessage();

        await using var connection = await _dataSource.OpenConnectionAsync();
        await using var tx = await connection.BeginTransactionAsync();

        await sut.InsertAsync(msg, new EricksonLopez.Outbox.Persistence.DbTransactionContext(tx));
        await tx.CommitAsync();

        var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM outbox.messages_dead_letters WHERE id = @Id", new { msg.Id });
        count.Should().Be(1);
    }

    [Fact]
    public async Task InsertAsync_Should_Handle_MemoryMarshal_EdgeCase_And_Nulls()
    {
        var sut = CreateSut();
        var msg = CreateDeadLetterMessage();
        
        // Force the fallback path by creating a ReadOnlyMemory that isn't cleanly array-backed from offset 0
        var fullPayload = new byte[] { 99, 123, 34, 107, 34, 58, 34, 118, 34, 125, 99 }; // 'c' + "{\"k\":\"v\"}" + 'c'
        var slicedPayload = new ReadOnlyMemory<byte>(fullPayload, 1, 9);
        
        var nullPropsMsg = new DeadLetterMessage(
            msg.Id,
            msg.OriginalMessageId,
            msg.MessageType,
            slicedPayload,
            null, // CorrelationId
            null, // CausationId
            slicedPayload, // Headers
            msg.CreatedAt,
            msg.DeadLetteredAt,
            msg.RetryCount,
            msg.Reason ?? "Unknown", // Reason
            null // LastError
        );

        await sut.InsertAsync(nullPropsMsg);

        await using var connection = await _dataSource.OpenConnectionAsync();
        var dbRecord = await connection.QuerySingleAsync("SELECT correlation_id, causation_id, error_reason, last_error FROM outbox.messages_dead_letters WHERE id = @Id", new { msg.Id });
        
        ((string?)dbRecord.correlation_id).Should().BeNull();
        ((string?)dbRecord.causation_id).Should().BeNull();
        ((string)dbRecord.error_reason).Should().Be(msg.Reason ?? "Unknown");
        ((string?)dbRecord.last_error).Should().BeNull();
        
        var payloadJson = await connection.ExecuteScalarAsync<string>("SELECT payload::text FROM outbox.messages_dead_letters WHERE id = @Id", new { msg.Id });
        payloadJson.Should().Be("{\"k\": \"v\"}");
        var headersJson = await connection.ExecuteScalarAsync<string>("SELECT headers_json::text FROM outbox.messages_dead_letters WHERE id = @Id", new { msg.Id });
        headersJson.Should().Be("{\"k\": \"v\"}");
    }

    [Fact]
    public async Task GetAsync_Should_Return_Messages()
    {
        var sut = CreateSut();
        var msg1 = CreateDeadLetterMessage();
        var msg2 = CreateDeadLetterMessage();
        
        // Ensure msg2 is strictly after msg1
        msg1 = msg1 with { DeadLetteredAt = DateTimeOffset.UtcNow.AddMinutes(-5) };
        msg2 = msg2 with { DeadLetteredAt = DateTimeOffset.UtcNow };

        await sut.InsertAsync(msg1);
        await sut.InsertAsync(msg2);

        var results = await sut.GetAsync(limit: 10);
        results.Should().HaveCount(2);

        var retrieved1 = results.FirstOrDefault(m => m.Id == msg1.Id)!;
        System.Text.Encoding.UTF8.GetString(retrieved1.Payload.Span).Should().Be("{}");
        retrieved1.CorrelationId.Should().Be(msg1.CorrelationId);
        retrieved1.CausationId.Should().Be(msg1.CausationId);
        retrieved1.LastError.Should().Be(msg1.LastError);
        
        var retrieved2 = results.FirstOrDefault(m => m.Id == msg2.Id)!;
        retrieved2.CorrelationId.Should().Be(msg2.CorrelationId);

        var afterResults = await sut.GetAsync(limit: 10, after: msg1.DeadLetteredAt);
        afterResults.Should().HaveCount(1);
        afterResults[0].Id.Should().Be(msg2.Id);
    }

    [Fact]
    public async Task GetAsync_WithCustomPayloadAndHeaders_ReturnsExactValues()
    {
        var sut = CreateSut();
        var msg = CreateDeadLetterMessage() with 
        { 
            Payload = System.Text.Encoding.UTF8.GetBytes("{\"custom\":\"dl_payload_123\"}"), 
            Headers = System.Text.Encoding.UTF8.GetBytes("{\"custom\":\"dl_headers_456\"}") 
        };

        await sut.InsertAsync(msg);

        var results = await sut.GetAsync(limit: 10);
        var retrieved = results.Single(m => m.Id == msg.Id);
        System.Text.Encoding.UTF8.GetString(retrieved.Payload.Span).Should().Be("{\"custom\": \"dl_payload_123\"}");
        System.Text.Encoding.UTF8.GetString(retrieved.Headers.Span).Should().Be("{\"custom\": \"dl_headers_456\"}");
    }

    [Fact]
    public async Task InsertAsync_WithSubArrayStartingAtZero_PersistsOnlySubArray()
    {
        var sut = CreateSut();
        var fullPayload = new byte[10] { 123, 34, 107, 34, 58, 34, 118, 34, 125, 0 }; // {"k":"v"} followed by 0
        var subPayload = new ReadOnlyMemory<byte>(fullPayload, 0, 9); // Offset = 0, Count = 9 < Length 10
        var msg = CreateDeadLetterMessage() with { Payload = subPayload, Headers = subPayload };

        await sut.InsertAsync(msg);

        var list = await sut.GetAsync(limit: 10);
        var retrieved = list.Single(x => x.Id == msg.Id);
        System.Text.Encoding.UTF8.GetString(retrieved.Payload.Span).Should().Be("{\"k\": \"v\"}");
        System.Text.Encoding.UTF8.GetString(retrieved.Headers.Span).Should().Be("{\"k\": \"v\"}");
    }
    
    [Fact]
    public async Task GetAsync_Should_Handle_Null_Columns_Properly()
    {
        var sut = CreateSut();
        var msg = CreateDeadLetterMessage();
        
        var nullPropsMsg = new DeadLetterMessage(
            msg.Id,
            msg.OriginalMessageId,
            msg.MessageType,
            msg.Payload,
            null,
            null,
            msg.Headers,
            msg.CreatedAt,
            msg.DeadLetteredAt,
            msg.RetryCount,
            msg.Reason,
            null
        );

        await sut.InsertAsync(nullPropsMsg);

        // Manually update the row to have actual DB NULLs for payload and headers to test fallback branch
        await using var connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync("UPDATE outbox.messages_dead_letters SET payload = NULL, headers_json = NULL WHERE id = @Id", new { msg.Id });

        var results = await sut.GetAsync();
        var fetched = results.Should().ContainSingle().Subject;
        
        fetched.CorrelationId.Should().BeNull();
        fetched.CausationId.Should().BeNull();
        fetched.LastError.Should().BeNull();
        
        // When DB is null, it should fallback to "{}"
        System.Text.Encoding.UTF8.GetString(fetched.Payload.Span).Should().Be("{}");
        System.Text.Encoding.UTF8.GetString(fetched.Headers.Span).Should().Be("{}");
    }

    [Fact]
    public async Task DeleteAsync_Should_Remove_Message()
    {
        var sut = CreateSut();
        var msg = CreateDeadLetterMessage();
        await sut.InsertAsync(msg);

        await sut.DeleteAsync(msg.Id);

        await using var connection = await _dataSource.OpenConnectionAsync();
        var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM outbox.messages_dead_letters WHERE id = @Id", new { msg.Id });
        count.Should().Be(0);
    }

    [Fact]
    public async Task PurgeAsync_Should_Remove_Old_Messages()
    {
        var sut = CreateSut();
        var oldMsg = CreateDeadLetterMessage() with { DeadLetteredAt = DateTimeOffset.UtcNow.AddDays(-10) };
        var newMsg = CreateDeadLetterMessage() with { DeadLetteredAt = DateTimeOffset.UtcNow };

        await sut.InsertAsync(oldMsg);
        await sut.InsertAsync(newMsg);

        await sut.PurgeAsync(DateTimeOffset.UtcNow.AddDays(-5));

        await using var connection = await _dataSource.OpenConnectionAsync();
        var countOld = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM outbox.messages_dead_letters WHERE id = @Id", new { oldMsg.Id });
        countOld.Should().Be(0);
        
        var countNew = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM outbox.messages_dead_letters WHERE id = @Id", new { newMsg.Id });
        countNew.Should().Be(1);
    }

    [Fact]
    public void IsFirstPartyImplementation_ShouldBeTrue()
    {
        var sut = CreateSut();
        sut.IsFirstPartyImplementation.Should().BeTrue();
    }

    [Fact]
    public void Constructor_NullParameters_ThrowsArgumentNullException()
    {
        var optionsMonitor = NSubstitute.Substitute.For<IOptionsMonitor<OutboxRuntimeOptions>>();
        optionsMonitor.CurrentValue.Returns(new OutboxRuntimeOptions { SchemaName = "outbox", TableName = "messages" });

        Action act1 = () => _ = new PostgreSqlDeadLetterRepository(null!, optionsMonitor);
        act1.Should().Throw<ArgumentNullException>().WithParameterName("dataSource");

        Action act2 = () => _ = new PostgreSqlDeadLetterRepository(_dataSource, null!);
        act2.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task InsertAsync_NullOrWhitespaceSchema_UsesPublicSchema(string? schema)
    {
        await using var connection = await _dataSource.OpenConnectionAsync();
        await connection.ExecuteAsync("CREATE TABLE IF NOT EXISTS public.messages_dead_letters (id uuid NOT NULL, original_message_id uuid NOT NULL, type text NOT NULL, payload jsonb NOT NULL, correlation_id text, causation_id text, headers_json jsonb NOT NULL, created_at timestamp with time zone NOT NULL, dead_lettered_at timestamp with time zone NOT NULL, retry_count integer NOT NULL, error_reason text NOT NULL, last_error text, CONSTRAINT pk_public_dead_letters PRIMARY KEY (id, dead_lettered_at));");

        var optionsMonitor = NSubstitute.Substitute.For<IOptionsMonitor<OutboxRuntimeOptions>>();
        optionsMonitor.CurrentValue.Returns(new OutboxRuntimeOptions { SchemaName = schema!, TableName = "messages" });

        var repo = new PostgreSqlDeadLetterRepository(_dataSource, optionsMonitor);
        var msg = CreateDeadLetterMessage();

        await repo.InsertAsync(msg);

        var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM public.messages_dead_letters WHERE id = @Id", new { msg.Id });
        count.Should().Be(1);
    }

    [Fact]
    public async Task InsertAsync_NonNpgsqlConnectionTransaction_ThrowsInvalidOperationException()
    {
        var sut = CreateSut();
        var msg = CreateDeadLetterMessage();
        
        var mockTx = Substitute.For<IOutboxTransactionContext>();
        mockTx.Connection.Returns(Substitute.For<System.Data.Common.DbConnection>());

        Func<Task> act = async () => await sut.InsertAsync(msg, mockTx);
        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.WithMessage("*NpgsqlConnection*");
    }

    [Fact]
    public async Task InsertAsync_NullReason_SetsUnknown()
    {
        var sut = CreateSut();
        var msg = CreateDeadLetterMessage() with { Reason = null! };

        await sut.InsertAsync(msg);

        await using var connection = await _dataSource.OpenConnectionAsync();
        var reason = await connection.ExecuteScalarAsync<string>("SELECT error_reason FROM outbox.messages_dead_letters WHERE id = @Id", new { msg.Id });
        reason.Should().Be("Unknown");
    }

    [Fact]
    public async Task InsertAsync_WithoutTransaction_DisposesConnectionProperly()
    {
        var connString = _fixture.Container.GetConnectionString() + ";Maximum Pool Size=2;Timeout=2";
        await using var limitedDs = NpgsqlDataSource.Create(connString);

        var optionsMonitor = Substitute.For<IOptionsMonitor<OutboxRuntimeOptions>>();
        optionsMonitor.CurrentValue.Returns(new OutboxRuntimeOptions { SchemaName = "outbox", TableName = "messages" });

        var repo = new PostgreSqlDeadLetterRepository(limitedDs, optionsMonitor);

        for (int i = 0; i < 5; i++)
        {
            var msg = CreateDeadLetterMessage();
            await repo.InsertAsync(msg);
        }
    }
}






