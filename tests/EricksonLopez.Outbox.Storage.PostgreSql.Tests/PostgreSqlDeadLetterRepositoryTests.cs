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
using Microsoft.Extensions.Options;

using EricksonLopez.Outbox.Storage.PostgreSql;
using Npgsql;
using Xunit;

namespace EricksonLopez.Outbox.Tests;

public class PostgreSqlDeadLetterRepositoryTests : IClassFixture<PostgreSqlContainerFixture>, IAsyncLifetime
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
        using var connection = await _dataSource.OpenConnectionAsync();

        const string schema = @"
            CREATE SCHEMA IF NOT EXISTS outbox;
            CREATE TABLE IF NOT EXISTS outbox.messages_dead_letters (
                id UUID PRIMARY KEY,
                original_message_id UUID NOT NULL,
                type VARCHAR(255) NOT NULL,
                payload JSONB,
                correlation_id VARCHAR(255),
                causation_id VARCHAR(255),
                headers_json JSONB,
                created_at TIMESTAMPTZ NOT NULL,
                dead_lettered_at TIMESTAMPTZ NOT NULL,
                retry_count INT NOT NULL,
                error_reason TEXT NOT NULL,
                last_error TEXT
            );
            TRUNCATE TABLE outbox.messages_dead_letters;";
        
        await connection.ExecuteAsync(schema);
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
        var fullPayload = new byte[] { 0, 123, 125, 0 }; // "{}"
        var slicedPayload = new ReadOnlyMemory<byte>(fullPayload, 1, 2);
        
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
        payloadJson.Should().Be("{}");
        var headersJson = await connection.ExecuteScalarAsync<string>("SELECT headers_json::text FROM outbox.messages_dead_letters WHERE id = @Id", new { msg.Id });
        headersJson.Should().Be("{}");
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
}



