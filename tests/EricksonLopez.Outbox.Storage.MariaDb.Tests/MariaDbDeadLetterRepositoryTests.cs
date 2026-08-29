// Copyright © Erickson Lopez. MIT License.
using System;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using AutoFixture;
using AutoFixture.AutoNSubstitute;
using AwesomeAssertions;
using Dapper;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Storage.MariaDb;
using Microsoft.Extensions.Options;
using MySqlConnector;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Storage.MariaDb.Tests;

[Collection("MariaDb")]
[Trait("Category", "Integration")]
public class MariaDbDeadLetterRepositoryTests : IAsyncLifetime
{
    private readonly MariaDbContainerFixture _fixture;
    private readonly IFixture _autoFixture;
    private readonly OutboxRuntimeOptions _options = new() { SchemaName = "", TableName = "outbox_messages" };

    public MariaDbDeadLetterRepositoryTests(MariaDbContainerFixture fixture)
    {
        _fixture = fixture;
        _autoFixture = new Fixture().Customize(new AutoNSubstituteCustomization());
    }

    public async Task InitializeAsync()
    {
        await MariaDbTestDatabase.EnsureSchemaAsync(_fixture.Container.GetConnectionString());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private MariaDbDeadLetterRepository CreateSut(OutboxRuntimeOptions? customOptions = null)
    {
        var mockedOptions = Substitute.For<IOptionsMonitor<OutboxRuntimeOptions>>();
        mockedOptions.CurrentValue.Returns(customOptions ?? _options);

        return new MariaDbDeadLetterRepository(
            () => new MySqlConnection(_fixture.Container.GetConnectionString()),
            mockedOptions);
    }

    [Fact]
    public void Constructor_NullConnectionFactory_ThrowsArgumentNullException()
    {
        var mockedOptions = Substitute.For<IOptionsMonitor<OutboxRuntimeOptions>>();
        mockedOptions.CurrentValue.Returns(_options);

        Action act = () => { _ = new MariaDbDeadLetterRepository(null!, mockedOptions); };
        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionFactory");
    }

    [Fact]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        Action act = () => { _ = new MariaDbDeadLetterRepository(() => new MySqlConnection(), null!); };
        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void IsFirstPartyImplementation_Should_Be_True()
    {
        var sut = CreateSut();
        sut.IsFirstPartyImplementation.Should().BeTrue();
    }

    [Fact]
    public async Task Operations_WithNonExistentSchema_ThrowsMySqlExceptionContainingSchemaName()
    {
        var custom = new OutboxRuntimeOptions { SchemaName = "non_existent_schema_xyz", TableName = "outbox_messages" };
        var sut = CreateSut(custom);
        Func<Task> act = async () => await sut.GetAsync(10);
        var ex = await act.Should().ThrowAsync<MySqlException>();
        ex.Which.Message.Should().Contain("non_existent_schema_xyz");
    }

    [Fact]
    public async Task InsertAsync_WithoutTransaction_DisposesConnection()
    {
        MySqlConnection? createdConn = null;
        var mockedOptions = Substitute.For<IOptionsMonitor<OutboxRuntimeOptions>>();
        mockedOptions.CurrentValue.Returns(_options);
        var sut = new MariaDbDeadLetterRepository(() => {
            createdConn = new MySqlConnection(_fixture.Container.GetConnectionString());
            return createdConn;
        }, mockedOptions);

        var msg = _autoFixture.Create<DeadLetterMessage>() with {
            Payload = "{}"u8.ToArray(),
            Headers = "{}"u8.ToArray(),
            CreatedAt = DateTimeOffset.UtcNow,
            DeadLetteredAt = DateTimeOffset.UtcNow
        };
        await sut.InsertAsync(msg);
        createdConn.Should().NotBeNull();
        createdConn!.State.Should().Be(ConnectionState.Closed);
    }

    [Fact]
    public async Task InsertAsync_Should_Persist_DeadLetterMessage()
    {
        var sut = CreateSut();
        var msg = _autoFixture.Create<DeadLetterMessage>() with
        {
            Payload = "{}"u8.ToArray(),
            Headers = "{}"u8.ToArray(),
            CreatedAt = DateTimeOffset.UtcNow,
            DeadLetteredAt = DateTimeOffset.UtcNow
        };

        await sut.InsertAsync(msg);

        await using var connection = new MySqlConnection(_fixture.Container.GetConnectionString());
        var count = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM outbox_messages_dead_letters WHERE id = @Id",
            new { Id = msg.Id.ToString() });
        count.Should().Be(1);
    }

    [Fact]
    public async Task InsertAsync_WithNullReason_DefaultsToUnknown()
    {
        var sut = CreateSut();
        var msg = _autoFixture.Create<DeadLetterMessage>() with { 
            Payload = "{}"u8.ToArray(),
            Headers = "{}"u8.ToArray(),
            Reason = null!,
            CreatedAt = DateTimeOffset.UtcNow,
            DeadLetteredAt = DateTimeOffset.UtcNow
        };

        await sut.InsertAsync(msg);

        await using var connection = new MySqlConnection(_fixture.Container.GetConnectionString());
        var reason = await connection.ExecuteScalarAsync<string>("SELECT reason FROM outbox_messages_dead_letters WHERE id = @Id", new { Id = msg.Id.ToString() });
        reason.Should().Be("Unknown");
    }

    [Fact]
    public async Task InsertAsync_Should_Not_Throw_If_Already_Exists()
    {
        var sut = CreateSut();
        var msg = _autoFixture.Create<DeadLetterMessage>() with
        {
            Payload = "{}"u8.ToArray(),
            Headers = "{}"u8.ToArray(),
            CreatedAt = DateTimeOffset.UtcNow,
            DeadLetteredAt = DateTimeOffset.UtcNow
        };

        await sut.InsertAsync(msg);
        await sut.InsertAsync(msg); // Should ignore via INSERT IGNORE

        await using var connection = new MySqlConnection(_fixture.Container.GetConnectionString());
        var count = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM outbox_messages_dead_letters WHERE id = @Id",
            new { Id = msg.Id.ToString() });
        count.Should().Be(1);
    }

    [Fact]
    public async Task InsertAsync_WithTransaction_Should_Use_Transaction()
    {
        var sut = CreateSut();
        var msg = _autoFixture.Create<DeadLetterMessage>() with
        {
            Payload = "{}"u8.ToArray(),
            Headers = "{}"u8.ToArray(),
            CreatedAt = DateTimeOffset.UtcNow,
            DeadLetteredAt = DateTimeOffset.UtcNow
        };

        await using var connection = new MySqlConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        await using var tx = await connection.BeginTransactionAsync();

        await sut.InsertAsync(msg, new DbTransactionContext(tx));

        await tx.RollbackAsync();

        await using var newConn = new MySqlConnection(_fixture.Container.GetConnectionString());
        var countAfterRollback = await newConn.ExecuteScalarAsync<int>(
            "SELECT COUNT(1) FROM outbox_messages_dead_letters WHERE id = @Id",
            new { Id = msg.Id.ToString() });
        countAfterRollback.Should().Be(0);
    }

    [Fact]
    public async Task GetAsync_Should_Return_Messages_With_Null_Mapping()
    {
        var sut = CreateSut();
        var msg1 = _autoFixture.Create<DeadLetterMessage>() with
        {
            Payload = "{}"u8.ToArray(),
            Headers = "{}"u8.ToArray(),
            CorrelationId = null,
            CausationId = null,
            LastError = null,
            Reason = "ExplicitReason",
            CreatedAt = DateTimeOffset.UtcNow,
            DeadLetteredAt = DateTimeOffset.UtcNow
        };
        var msg2 = _autoFixture.Create<DeadLetterMessage>() with
        {
            Payload = System.Text.Encoding.UTF8.GetBytes("{\"mariadb\":\"custom_data\"}"),
            Headers = System.Text.Encoding.UTF8.GetBytes("{\"mariadb\":\"custom_headers\"}"),
            CorrelationId = "corr",
            CausationId = "caus",
            LastError = "err",
            Reason = "AnotherReason",
            CreatedAt = DateTimeOffset.UtcNow,
            DeadLetteredAt = DateTimeOffset.UtcNow
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
        System.Text.Encoding.UTF8.GetString(retrieved2.Payload.Span).Should().Be("{\"mariadb\":\"custom_data\"}");
        System.Text.Encoding.UTF8.GetString(retrieved2.Headers.Span).Should().Be("{\"mariadb\":\"custom_headers\"}");
    }

    [Fact]
    public async Task GetAsync_WithNullDbFields_ReturnsDefaultValues()
    {
        var id = Guid.NewGuid();
        var origId = Guid.NewGuid();
        await using var connection = new MySqlConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        await connection.ExecuteAsync(@"
            INSERT INTO outbox_messages_dead_letters 
            (id, original_message_id, type, payload, correlation_id, causation_id, headers_json, created_at, dead_lettered_at, retry_count, reason, last_error)
            VALUES (@Id, @OrigId, 'test.type', NULL, NULL, NULL, NULL, UTC_TIMESTAMP(), UTC_TIMESTAMP(), 0, NULL, NULL)",
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
    public async Task GetAsync_Should_Filter_By_After_Date()
    {
        var sut = CreateSut();
        var msg = _autoFixture.Create<DeadLetterMessage>() with
        {
            Payload = "{}"u8.ToArray(),
            Headers = "{}"u8.ToArray(),
            CreatedAt = DateTimeOffset.UtcNow,
            DeadLetteredAt = DateTimeOffset.UtcNow.AddDays(-2)
        };
        await sut.InsertAsync(msg);

        var results = await sut.GetAsync(after: DateTimeOffset.UtcNow.AddDays(1));
        results.Should().NotContain(m => m.Id == msg.Id);
    }

    [Fact]
    public async Task DeleteAsync_Should_Remove_Message()
    {
        var sut = CreateSut();
        var msg = _autoFixture.Create<DeadLetterMessage>() with
        {
            Payload = "{}"u8.ToArray(),
            Headers = "{}"u8.ToArray(),
            CreatedAt = DateTimeOffset.UtcNow,
            DeadLetteredAt = DateTimeOffset.UtcNow
        };
        await sut.InsertAsync(msg);

        await sut.DeleteAsync(msg.Id);

        var results = await sut.GetAsync();
        results.Should().NotContain(m => m.Id == msg.Id);
    }

    [Fact]
    public async Task PurgeAsync_Should_Remove_Old_Messages()
    {
        var sut = CreateSut();
        var msg = _autoFixture.Create<DeadLetterMessage>() with
        {
            Payload = "{}"u8.ToArray(),
            Headers = "{}"u8.ToArray(),
            CreatedAt = DateTimeOffset.UtcNow,
            DeadLetteredAt = DateTimeOffset.UtcNow.AddDays(-2)
        };
        await sut.InsertAsync(msg);

        await sut.PurgeAsync(DateTimeOffset.UtcNow.AddDays(1));

        var results = await sut.GetAsync();
        results.Should().NotContain(m => m.Id == msg.Id);
    }
}
