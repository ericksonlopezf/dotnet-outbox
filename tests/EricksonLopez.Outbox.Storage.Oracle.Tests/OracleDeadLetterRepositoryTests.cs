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
using EricksonLopez.Outbox.Storage.Oracle;
using Microsoft.Extensions.Options;
using NSubstitute;
using Oracle.ManagedDataAccess.Client;
using Xunit;

namespace EricksonLopez.Outbox.Storage.Oracle.Tests;

[Collection("Oracle")]
[Trait("Category", "Integration")]
public class OracleDeadLetterRepositoryTests : IAsyncLifetime
{
    private readonly OracleContainerFixture _fixture;
    private readonly IFixture _autoFixture;
    private readonly OutboxRuntimeOptions _options = new() { SchemaName = string.Empty, TableName = "messages" };

    public OracleDeadLetterRepositoryTests(OracleContainerFixture fixture)
    {
        _fixture = fixture;
        _autoFixture = new Fixture().Customize(new AutoNSubstituteCustomization());
    }

    public async Task InitializeAsync()
    {
        await OracleTestDatabase.EnsureSchemaAsync(_fixture.Container.GetConnectionString());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private OracleDeadLetterRepository CreateSut(OutboxRuntimeOptions? customOptions = null)
    {
        var optionsMonitor = Substitute.For<IOptionsMonitor<OutboxRuntimeOptions>>();
        optionsMonitor.CurrentValue.Returns(customOptions ?? _options);
        return new OracleDeadLetterRepository(() => new OracleConnection(_fixture.Container.GetConnectionString()), optionsMonitor);
    }

    [Fact]
    public void Constructor_NullConnectionFactory_ThrowsArgumentNullException()
    {
        var optionsMonitor = Substitute.For<IOptionsMonitor<OutboxRuntimeOptions>>();
        optionsMonitor.CurrentValue.Returns(_options);

        Action act = () => { _ = new OracleDeadLetterRepository(null!, optionsMonitor); };
        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionFactory");
    }

    [Fact]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        Action act = () => { _ = new OracleDeadLetterRepository(() => new OracleConnection(), null!); };
        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void IsFirstPartyImplementation_Should_Be_True()
    {
        var sut = CreateSut();
        sut.IsFirstPartyImplementation.Should().BeTrue();
    }

    [Fact]
    public async Task Operations_WithNonExistentSchema_ThrowsOracleException()
    {
        var custom = new OutboxRuntimeOptions { SchemaName = "NON_EXISTENT_SCHEMA_XYZ", TableName = "messages" };
        var sut = CreateSut(custom);
        Func<Task> act = async () => await sut.GetAsync(10);
        await act.Should().ThrowAsync<OracleException>();
    }

    [Fact]
    public async Task InsertAsync_WithoutTransaction_DisposesConnection()
    {
        OracleConnection? createdConn = null;
        var optionsMonitor = Substitute.For<IOptionsMonitor<OutboxRuntimeOptions>>();
        optionsMonitor.CurrentValue.Returns(_options);
        var sut = new OracleDeadLetterRepository(() => {
            createdConn = new OracleConnection(_fixture.Container.GetConnectionString());
            return createdConn;
        }, optionsMonitor);

        var msg = _autoFixture.Create<DeadLetterMessage>() with { 
            Payload = "{}"u8.ToArray(),
            Headers = "{}"u8.ToArray()
        };
        await sut.InsertAsync(msg);
        createdConn.Should().NotBeNull();
        createdConn!.State.Should().Be(ConnectionState.Closed);
    }

    [Fact]
    public async Task InsertAsync_Should_Persist_DeadLetterMessage()
    {
        var sut = CreateSut();
        var msg = _autoFixture.Create<DeadLetterMessage>() with { 
            Payload = "{}"u8.ToArray(),
            Headers = "{}"u8.ToArray()
        };

        await sut.InsertAsync(msg);

        await using var connection = new OracleConnection(_fixture.Container.GetConnectionString());
        var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM \"MESSAGES_DEAD_LETTERS\" WHERE id = :Id", new { Id = msg.Id.ToString("N") });
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

        await using var connection = new OracleConnection(_fixture.Container.GetConnectionString());
        var reason = await connection.ExecuteScalarAsync<string>("SELECT reason FROM \"MESSAGES_DEAD_LETTERS\" WHERE id = :Id", new { Id = msg.Id.ToString("N") });
        reason.Should().Be("Unknown");
    }

    [Fact]
    public async Task InsertAsync_Should_Not_Throw_If_Already_Exists()
    {
        var sut = CreateSut();
        var msg = _autoFixture.Create<DeadLetterMessage>() with { 
            Payload = "{}"u8.ToArray(),
            Headers = "{}"u8.ToArray()
        };

        await sut.InsertAsync(msg);
        await sut.InsertAsync(msg); // Should ignore via WHERE NOT EXISTS

        await using var connection = new OracleConnection(_fixture.Container.GetConnectionString());
        var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM \"MESSAGES_DEAD_LETTERS\" WHERE id = :Id", new { Id = msg.Id.ToString("N") });
        count.Should().Be(1);
    }

    [Fact]
    public async Task InsertAsync_WithTransaction_Should_Use_Transaction()
    {
        var sut = CreateSut();
        var msg = _autoFixture.Create<DeadLetterMessage>() with { 
            Payload = "{}"u8.ToArray(),
            Headers = "{}"u8.ToArray()
        };

        await using var connection = new OracleConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        await using var tx = connection.BeginTransaction();

        await sut.InsertAsync(msg, new DbTransactionContext(tx));

        await tx.RollbackAsync();

        await using var newConn = new OracleConnection(_fixture.Container.GetConnectionString());
        var countAfterRollback = await newConn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM \"MESSAGES_DEAD_LETTERS\" WHERE id = :Id", new { Id = msg.Id.ToString("N") });
        countAfterRollback.Should().Be(0);
    }

    [Fact]
    public async Task GetAsync_Should_Return_Messages_With_Null_Mapping()
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
            Payload = System.Text.Encoding.UTF8.GetBytes("{\"oracle\":\"custom_data\"}"),
            Headers = System.Text.Encoding.UTF8.GetBytes("{\"oracle\":\"custom_headers\"}"),
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
        System.Text.Encoding.UTF8.GetString(retrieved2.Payload.Span).Should().Be("{\"oracle\":\"custom_data\"}");
        System.Text.Encoding.UTF8.GetString(retrieved2.Headers.Span).Should().Be("{\"oracle\":\"custom_headers\"}");
    }

    [Fact]
    public async Task GetAsync_WithNullDbFields_ReturnsDefaultValues()
    {
        var id = Guid.NewGuid();
        var origId = Guid.NewGuid();
        await using var connection = new OracleConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        await connection.ExecuteAsync(@"
            INSERT INTO ""MESSAGES_DEAD_LETTERS"" 
            (id, original_message_id, type, payload, correlation_id, causation_id, headers_json, created_at, dead_lettered_at, retry_count, reason, last_error)
            VALUES (:Id, :OrigId, 'test.type', NULL, NULL, NULL, NULL, CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, 0, NULL, NULL)",
            new { Id = id.ToString("N"), OrigId = origId.ToString("N") });

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
    public async Task DeleteAsync_Should_Remove_Message()
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
    public async Task PurgeAsync_Should_Remove_Old_Messages()
    {
        var sut = CreateSut();
        var msg = _autoFixture.Create<DeadLetterMessage>() with { 
            Payload = "{}"u8.ToArray(),
            Headers = "{}"u8.ToArray(),
            DeadLetteredAt = DateTimeOffset.UtcNow.AddDays(-2)
        };
        await sut.InsertAsync(msg);

        await sut.PurgeAsync(DateTimeOffset.UtcNow.AddDays(1));

        var results = await sut.GetAsync();
        results.Should().NotContain(m => m.Id == msg.Id);
    }

    [Fact]
    public async Task GetAsync_WithLimit_RestrictsReturnedRowCount()
    {
        var sut = CreateSut();
        var msg1 = _autoFixture.Create<DeadLetterMessage>() with { Payload = "{}"u8.ToArray(), Headers = "{}"u8.ToArray(), DeadLetteredAt = DateTimeOffset.UtcNow.AddMinutes(-10) };
        var msg2 = _autoFixture.Create<DeadLetterMessage>() with { Payload = "{}"u8.ToArray(), Headers = "{}"u8.ToArray(), DeadLetteredAt = DateTimeOffset.UtcNow.AddMinutes(-5) };
        var msg3 = _autoFixture.Create<DeadLetterMessage>() with { Payload = "{}"u8.ToArray(), Headers = "{}"u8.ToArray(), DeadLetteredAt = DateTimeOffset.UtcNow.AddMinutes(-1) };

        await sut.InsertAsync(msg1);
        await sut.InsertAsync(msg2);
        await sut.InsertAsync(msg3);

        var limitedResults = await sut.GetAsync(limit: 2);
        limitedResults.Should().HaveCount(2);
    }
}
