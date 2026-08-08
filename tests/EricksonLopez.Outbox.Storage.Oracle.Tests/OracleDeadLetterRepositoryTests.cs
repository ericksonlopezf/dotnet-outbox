using System;
using System.Linq;
using System.Threading.Tasks;
using AutoFixture;
using AutoFixture.AutoNSubstitute;
using Dapper;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Storage.Oracle;
using AwesomeAssertions;
using Oracle.ManagedDataAccess.Client;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Tests;

public class OracleDeadLetterRepositoryTests : IClassFixture<OracleContainerFixture>, IAsyncLifetime
{
    private readonly OracleContainerFixture _fixture;
    private readonly IFixture _autoFixture;

    public OracleDeadLetterRepositoryTests(OracleContainerFixture fixture)
    {
        _fixture = fixture;
        _autoFixture = new Fixture().Customize(new AutoNSubstituteCustomization());
    }

    public async Task InitializeAsync()
    {
        using var connection = new OracleConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();

        const string schema = @"
            BEGIN
                EXECUTE IMMEDIATE 'CREATE TABLE ""MESSAGES_DEAD_LETTERS"" (
                    id VARCHAR2(32) PRIMARY KEY,
                    original_message_id VARCHAR2(32) NOT NULL,
                    type VARCHAR2(255) NOT NULL,
                    payload CLOB,
                    correlation_id VARCHAR2(255),
                    causation_id VARCHAR2(255),
                    headers_json CLOB,
                    created_at TIMESTAMP(6) WITH TIME ZONE NOT NULL,
                    dead_lettered_at TIMESTAMP(6) WITH TIME ZONE DEFAULT CURRENT_TIMESTAMP NOT NULL,
                    retry_count NUMBER(10) DEFAULT 0 NOT NULL,
                    reason VARCHAR2(2000),
                    last_error CLOB
                )';
            EXCEPTION
                WHEN OTHERS THEN
                    IF SQLCODE != -955 THEN
                        RAISE;
                    END IF;
            END;";
        
        await connection.ExecuteAsync(schema);
        await connection.ExecuteAsync("TRUNCATE TABLE \"MESSAGES_DEAD_LETTERS\"");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public void IsFirstPartyImplementation_Should_Be_True()
    {
        var sut = CreateSut();
        sut.IsFirstPartyImplementation.Should().BeTrue();
    }

    private OracleDeadLetterRepository CreateSut()
    {
        var optionsMonitor = Substitute.For<IOptionsMonitor<OutboxRuntimeOptions>>();
        optionsMonitor.CurrentValue.Returns(new OutboxRuntimeOptions { SchemaName = string.Empty, TableName = "messages" });
        return new OracleDeadLetterRepository(() => new OracleConnection(_fixture.Container.GetConnectionString()), optionsMonitor);
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

        await sut.InsertAsync(msg, new EricksonLopez.Outbox.Persistence.DbTransactionContext(tx));

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
            LastError = null
        };
        var msg2 = _autoFixture.Create<DeadLetterMessage>() with { 
            Payload = "{}"u8.ToArray(),
            Headers = "{}"u8.ToArray(),
            CorrelationId = "corr",
            CausationId = "caus",
            LastError = "err"
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

        var retrieved2 = results.FirstOrDefault(m => m.Id == msg2.Id)!;
        retrieved2.CorrelationId.Should().Be("corr");
        retrieved2.CausationId.Should().Be("caus");
        retrieved2.LastError.Should().Be("err");
    }

    [Fact]
    public async Task GetAsync_Should_Filter_By_After_Date()
    {
        var sut = CreateSut();
        var msg = _autoFixture.Create<DeadLetterMessage>() with { 
            Payload = "{}"u8.ToArray(),
            Headers = "{}"u8.ToArray(),
            DeadLetteredAt = DateTimeOffset.UtcNow
        };
        await sut.InsertAsync(msg);

        var results = await sut.GetAsync(after: DateTimeOffset.UtcNow.AddDays(1));
        
        results.Should().NotContain(m => m.Id == msg.Id);
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
            DeadLetteredAt = DateTimeOffset.UtcNow
        };
        await sut.InsertAsync(msg);

        await sut.PurgeAsync(DateTimeOffset.UtcNow.AddDays(1));

        var results = await sut.GetAsync();
        results.Should().NotContain(m => m.Id == msg.Id);
    }
}
