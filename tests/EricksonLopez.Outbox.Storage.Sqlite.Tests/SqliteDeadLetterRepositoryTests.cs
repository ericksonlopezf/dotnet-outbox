// Copyright © Erickson Lopez. MIT License.
using System;
using System.Data;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoFixture;
using AutoFixture.AutoNSubstitute;
using AwesomeAssertions;
using Dapper;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Storage.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Storage.Sqlite.Tests;

public class SqliteDeadLetterRepositoryTests : IDisposable
{
    private readonly IFixture _autoFixture;
    private readonly string _connectionString;
    private readonly SqliteConnection _connection;
    private readonly OutboxRuntimeOptions _options = new() { TableName = "messages" };

    public SqliteDeadLetterRepositoryTests()
    {
        _autoFixture = new Fixture().Customize(new AutoNSubstituteCustomization());
        _connectionString = $"Data Source=outboxdl_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _connection = new SqliteConnection(_connectionString);
        _connection.Open();

        SqliteTestDatabase.EnsureSchema(_connection);
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private SqliteDeadLetterRepository CreateSut(OutboxRuntimeOptions? customOptions = null)
    {
        var mockedOptions = Substitute.For<IOptionsMonitor<OutboxRuntimeOptions>>();
        mockedOptions.CurrentValue.Returns(customOptions ?? _options);
            
        return new SqliteDeadLetterRepository(() => new SqliteConnection(_connectionString), mockedOptions);
    }

    [Fact]
    public void Constructor_NullConnectionFactory_ThrowsArgumentNullException()
    {
        var mockedOptions = Substitute.For<IOptionsMonitor<OutboxRuntimeOptions>>();
        mockedOptions.CurrentValue.Returns(_options);

        Action act = () => { _ = new SqliteDeadLetterRepository(null!, mockedOptions); };
        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionFactory");
    }

    [Fact]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        Action act = () => { _ = new SqliteDeadLetterRepository(() => new SqliteConnection(), null!); };
        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void IsFirstPartyImplementation_Should_Be_True()
    {
        var sut = CreateSut();
        sut.IsFirstPartyImplementation.Should().BeTrue();
    }

    [Fact]
    public async Task InsertAsync_WithoutTransaction_DisposesConnection()
    {
        SqliteConnection? createdConn = null;
        var mockedOptions = Substitute.For<IOptionsMonitor<OutboxRuntimeOptions>>();
        mockedOptions.CurrentValue.Returns(_options);
        var sut = new SqliteDeadLetterRepository(() => {
            createdConn = new SqliteConnection(_connectionString);
            return createdConn;
        }, mockedOptions);

        var msg = _autoFixture.Create<DeadLetterMessage>() with { 
            Payload = "{}"u8.ToArray(),
            Headers = "{}"u8.ToArray()
        };
        await sut.InsertAsync(msg);
        createdConn.Should().NotBeNull();
        createdConn!.State.Should().Be(ConnectionState.Closed);
    }

    [Fact]
    public async Task InsertAsync_WhenValidMessage_PersistsDeadLetter()
    {
        var sut = CreateSut();
        var msg = _autoFixture.Create<DeadLetterMessage>() with { 
            Payload = "{}"u8.ToArray(),
            Headers = "{}"u8.ToArray()
        };

        await sut.InsertAsync(msg);

        await using var connection = new SqliteConnection(_connectionString);
        var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM messages_dead_letters WHERE id = @Id", new { Id = msg.Id.ToString() });
        count.Should().Be(1);
    }

    [Fact]
    public async Task InsertAsync_WithNullReason_DefaultsToUnknown()
    {
        var sut = CreateSut();
        var msg = _autoFixture.Create<DeadLetterMessage>() with { 
            Payload = "{}"u8.ToArray(),
            Headers = "{}"u8.ToArray(),
            Reason = null!
        };

        await sut.InsertAsync(msg);

        await using var connection = new SqliteConnection(_connectionString);
        var reason = await connection.ExecuteScalarAsync<string>("SELECT reason FROM messages_dead_letters WHERE id = @Id", new { Id = msg.Id.ToString() });
        reason.Should().Be("Unknown");
    }

    [Fact]
    public async Task InsertAsync_WhenMessageAlreadyExists_IgnoresDuplicateWithoutThrowing()
    {
        var sut = CreateSut();
        var msg = _autoFixture.Create<DeadLetterMessage>() with { 
            Payload = "{}"u8.ToArray(),
            Headers = "{}"u8.ToArray()
        };

        await sut.InsertAsync(msg);
        await sut.InsertAsync(msg); // Should ignore via INSERT OR IGNORE

        await using var connection = new SqliteConnection(_connectionString);
        var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM messages_dead_letters WHERE id = @Id", new { Id = msg.Id.ToString() });
        count.Should().Be(1);
    }

    [Fact]
    public async Task InsertAsync_WhenTransactionRolledBack_RollsBackInsertion()
    {
        var sut = CreateSut();
        var msg = _autoFixture.Create<DeadLetterMessage>() with { 
            Payload = "{}"u8.ToArray(),
            Headers = "{}"u8.ToArray()
        };

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await using var tx = connection.BeginTransaction();

        await sut.InsertAsync(msg, new DbTransactionContext(tx));

        await tx.RollbackAsync();

        await using var newConn = new SqliteConnection(_connectionString);
        var countAfterRollback = await newConn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM messages_dead_letters WHERE id = @Id", new { Id = msg.Id.ToString() });
        countAfterRollback.Should().Be(0);
    }

    [Fact]
    public async Task GetAsync_WhenMessagesExist_ReturnsAllWithProperMapping()
    {
        var sut = CreateSut();
        var msg1 = _autoFixture.Create<DeadLetterMessage>() with { 
            Payload = "{}"u8.ToArray(),
            Headers = "{}"u8.ToArray(),
            CorrelationId = null,
            CausationId = null,
            LastError = null,
            Reason = "ExplicitReason"
        };
        var msg2 = _autoFixture.Create<DeadLetterMessage>() with { 
            Payload = System.Text.Encoding.UTF8.GetBytes("{\"sqlite\":\"custom_data\"}"),
            Headers = System.Text.Encoding.UTF8.GetBytes("{\"sqlite\":\"custom_headers\"}"),
            CorrelationId = "corr",
            CausationId = "caus",
            LastError = "err",
            Reason = "AnotherReason"
        };

        await sut.InsertAsync(msg1);
        await sut.InsertAsync(msg2);

        var results = await sut.GetAsync(10);
        
        results.Should().Contain(m => m.Id == msg1.Id);
        results.Should().Contain(m => m.Id == msg2.Id);

        var retrieved1 = results.FirstOrDefault(m => m.Id == msg1.Id)!;
        retrieved1.CorrelationId.Should().BeNull();
        retrieved1.CausationId.Should().BeNull();
        retrieved1.LastError.Should().BeNull();
        retrieved1.Reason.Should().Be("ExplicitReason");

        var retrieved2 = results.FirstOrDefault(m => m.Id == msg2.Id)!;
        retrieved2.CorrelationId.Should().Be("corr");
        retrieved2.CausationId.Should().Be("caus");
        retrieved2.LastError.Should().Be("err");
        retrieved2.Reason.Should().Be("AnotherReason");
        System.Text.Encoding.UTF8.GetString(retrieved2.Payload.Span).Should().Be("{\"sqlite\":\"custom_data\"}");
        System.Text.Encoding.UTF8.GetString(retrieved2.Headers.Span).Should().Be("{\"sqlite\":\"custom_headers\"}");
    }

    [Fact]
    public async Task GetAsync_WithNullDbFields_ReturnsDefaultValues()
    {
        var id = Guid.NewGuid();
        var origId = Guid.NewGuid();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        await connection.ExecuteAsync(@"
            INSERT INTO messages_dead_letters 
            (id, original_message_id, type, payload, correlation_id, causation_id, headers_json, created_at, dead_lettered_at, retry_count, reason, last_error)
            VALUES (@Id, @OrigId, 'test.type', NULL, NULL, NULL, NULL, datetime('now'), datetime('now'), 0, NULL, NULL)",
            new { Id = id.ToString(), OrigId = origId.ToString() });

        var sut = CreateSut();
        var results = await sut.GetAsync(100);
        var msg = results.FirstOrDefault(m => m.Id == id);
        msg.Should().NotBeNull();
        msg!.Reason.Should().Be("Unknown");
        System.Text.Encoding.UTF8.GetString(msg.Payload.Span).Should().Be("{}");
        System.Text.Encoding.UTF8.GetString(msg.Headers.Span).Should().Be("{}");
        msg.CorrelationId.Should().BeNull();
        msg.CausationId.Should().BeNull();
        msg.LastError.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_WhenFilterByAfterDate_FiltersCorrectly()
    {
        var sut = CreateSut();
        var targetDate = DateTimeOffset.UtcNow.AddDays(-2);
        var msg = _autoFixture.Create<DeadLetterMessage>() with { 
            Payload = "{}"u8.ToArray(),
            Headers = "{}"u8.ToArray(),
            DeadLetteredAt = targetDate
        };
        await sut.InsertAsync(msg);

        var resultsBefore = await sut.GetAsync(after: targetDate.AddDays(-1));
        resultsBefore.Should().Contain(m => m.Id == msg.Id);

        var resultsAfter = await sut.GetAsync(after: targetDate.AddDays(1));
        resultsAfter.Should().NotContain(m => m.Id == msg.Id);
    }

    [Fact]
    public async Task DeleteAsync_WhenMessageExists_DeletesFromDatabase()
    {
        var sut = CreateSut();
        var msg = _autoFixture.Create<DeadLetterMessage>() with { 
            Payload = "{}"u8.ToArray(),
            Headers = "{}"u8.ToArray()
        };
        await sut.InsertAsync(msg);

        await sut.DeleteAsync(msg.Id);

        var results = await sut.GetAsync();
        results.Should().NotContain(m => m.Id == msg.Id);
    }

    [Fact]
    public async Task PurgeAsync_WhenOldMessagesExist_DeletesOlderThanCutoff()
    {
        var sut = CreateSut();
        var oldMsg = _autoFixture.Create<DeadLetterMessage>() with { 
            Payload = "{}"u8.ToArray(),
            Headers = "{}"u8.ToArray(),
            DeadLetteredAt = DateTimeOffset.UtcNow.AddDays(-5)
        };
        var newMsg = _autoFixture.Create<DeadLetterMessage>() with { 
            Payload = "{}"u8.ToArray(),
            Headers = "{}"u8.ToArray(),
            DeadLetteredAt = DateTimeOffset.UtcNow.AddDays(5)
        };
        await sut.InsertAsync(oldMsg);
        await sut.InsertAsync(newMsg);

        await sut.PurgeAsync(DateTimeOffset.UtcNow);

        var results = await sut.GetAsync();
        results.Should().NotContain(m => m.Id == oldMsg.Id);
        results.Should().Contain(m => m.Id == newMsg.Id);
    }
}
