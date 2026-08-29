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
using EricksonLopez.Outbox.Storage.SqlServer;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Storage.SqlServer.Tests;

[Collection("SqlServer")]
[Trait("Category", "Integration")]
public class SqlServerDeadLetterRepositoryTests : IAsyncLifetime
{
    private readonly SqlServerContainerFixture _fixture;
    private readonly IFixture _autoFixture;

    public SqlServerDeadLetterRepositoryTests(SqlServerContainerFixture fixture)
    {
        _fixture = fixture;
        _autoFixture = new Fixture().Customize(new AutoNSubstituteCustomization());
    }

    public async Task InitializeAsync()
    {
        await SqlServerTestDatabase.EnsureSchemaAsync(_fixture.Container.GetConnectionString());
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private SqlServerDeadLetterRepository CreateSut(OutboxRuntimeOptions? customOptions = null)
    {
        var opt = customOptions ?? new OutboxRuntimeOptions();
        var options = new OptionsMonitor<OutboxRuntimeOptions>(
            new OptionsFactory<OutboxRuntimeOptions>(
                new[] { new ConfigureOptions<OutboxRuntimeOptions>(o => {
                    o.SchemaName = opt.SchemaName;
                    o.TableName = opt.TableName;
                }) },
                Array.Empty<IPostConfigureOptions<OutboxRuntimeOptions>>()),
            Array.Empty<IOptionsChangeTokenSource<OutboxRuntimeOptions>>(),
            new OptionsCache<OutboxRuntimeOptions>());
        return new SqlServerDeadLetterRepository(() => new SqlConnection(_fixture.Container.GetConnectionString()), options);
    }

    [Fact]
    public void Constructor_NullConnectionFactory_ThrowsArgumentNullException()
    {
        var options = new OptionsMonitor<OutboxRuntimeOptions>(
            new OptionsFactory<OutboxRuntimeOptions>(Array.Empty<IConfigureOptions<OutboxRuntimeOptions>>(), Array.Empty<IPostConfigureOptions<OutboxRuntimeOptions>>()),
            Array.Empty<IOptionsChangeTokenSource<OutboxRuntimeOptions>>(),
            new OptionsCache<OutboxRuntimeOptions>());

        Action act = () => { _ = new SqlServerDeadLetterRepository(null!, options); };
        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionFactory");
    }

    [Fact]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        Action act = () => { _ = new SqlServerDeadLetterRepository(() => new SqlConnection(), null!); };
        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void IsFirstPartyImplementation_ReturnsTrue()
    {
        var sut = CreateSut();
        sut.IsFirstPartyImplementation.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_NullOrWhitespaceSchemaName_DefaultsToDbo(string? schemaName)
    {
        var custom = new OutboxRuntimeOptions { SchemaName = schemaName!, TableName = "messages" };
        var sut = CreateSut(custom);
        sut.Should().NotBeNull();
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

        await using var connection = new SqlConnection(_fixture.Container.GetConnectionString());
        var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM [outbox].[messages_dead_letters] WHERE id = @Id", new { msg.Id });
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
        await sut.InsertAsync(msg); // Should ignore via IF NOT EXISTS

        await using var connection = new SqlConnection(_fixture.Container.GetConnectionString());
        var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM [outbox].[messages_dead_letters] WHERE id = @Id", new { msg.Id });
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

        await using var connection = new SqlConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        await using var tx = connection.BeginTransaction();

        await sut.InsertAsync(msg, new DbTransactionContext(tx));

        await tx.RollbackAsync();

        await using var newConn = new SqlConnection(_fixture.Container.GetConnectionString());
        var countAfterRollback = await newConn.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM [outbox].[messages_dead_letters] WHERE id = @Id", new { msg.Id });
        countAfterRollback.Should().Be(0);
    }

    [Fact]
    public async Task InsertAsync_WithInvalidTransactionConnection_ThrowsInvalidOperationException()
    {
        var sut = CreateSut();
        var msg = _autoFixture.Create<DeadLetterMessage>() with {
            Payload = "{}"u8.ToArray(),
            Headers = "{}"u8.ToArray()
        };

        var invalidTx = Substitute.For<IOutboxTransactionContext>();
        invalidTx.Connection.Returns(Substitute.For<IDbConnection>());

        Func<Task> act = async () => await sut.InsertAsync(msg, invalidTx);
        var ex = await act.Should().ThrowAsync<InvalidOperationException>();
        ex.WithMessage("Connection must be a SqlConnection");
    }

    [Fact]
    public async Task InsertAsync_WithoutTransaction_Should_Open_Connection_And_Dispose()
    {
        var sut = CreateSut();
        var msg = _autoFixture.Create<DeadLetterMessage>() with { 
            Payload = "{}"u8.ToArray(),
            Headers = "{}"u8.ToArray()
        };

        // No transaction provided
        await sut.InsertAsync(msg);
        
        await using var connection = new SqlConnection(_fixture.Container.GetConnectionString());
        var count = await connection.ExecuteScalarAsync<int>("SELECT COUNT(1) FROM [outbox].[messages_dead_letters] WHERE id = @Id", new { msg.Id });
        count.Should().Be(1);
    }

    [Fact]
    public async Task InsertAsync_WithNullReason_StoresUnknown()
    {
        var sut = CreateSut();
        var msg = default(DeadLetterMessage) with {
            Id = Guid.NewGuid(),
            OriginalMessageId = Guid.NewGuid(),
            MessageType = "test.type",
            Payload = "{}"u8.ToArray(),
            Headers = "{}"u8.ToArray(),
            CreatedAt = DateTimeOffset.UtcNow
        };

        await sut.InsertAsync(msg);

        await using var connection = new SqlConnection(_fixture.Container.GetConnectionString());
        var reason = await connection.ExecuteScalarAsync<string>("SELECT reason FROM [outbox].[messages_dead_letters] WHERE id = @Id", new { msg.Id });
        reason.Should().Be("Unknown");
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
            Payload = System.Text.Encoding.UTF8.GetBytes("{\"custom\":\"payload123\"}"),
            Headers = System.Text.Encoding.UTF8.GetBytes("{\"custom\":\"headers456\"}"),
            CorrelationId = "corr",
            CausationId = "caus",
            LastError = "err",
            Reason = "AnotherReason"
        };

        await sut.InsertAsync(msg1);
        await sut.InsertAsync(msg2);

        // Act
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
        System.Text.Encoding.UTF8.GetString(retrieved2.Payload.Span).Should().Be("{\"custom\":\"payload123\"}");
        System.Text.Encoding.UTF8.GetString(retrieved2.Headers.Span).Should().Be("{\"custom\":\"headers456\"}");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("dbo")]
    [InlineData("outbox")]
    public async Task Operations_WithConfiguredSchema_TargetsConfiguredSchemaTable(string? schema)
    {
        var custom = new OutboxRuntimeOptions { SchemaName = schema!, TableName = "messages" };
        var sut = CreateSut(custom);
        var msg = _autoFixture.Create<DeadLetterMessage>() with {
            Payload = "{}"u8.ToArray(),
            Headers = "{}"u8.ToArray()
        };
        await sut.InsertAsync(msg);
        var results = await sut.GetAsync(10);
        results.Should().Contain(m => m.Id == msg.Id);
    }

    [Fact]
    public async Task GetAsync_WithNullDbFields_ReturnsDefaultValues()
    {
        var id = Guid.NewGuid();
        var origId = Guid.NewGuid();
        await using var connection = new SqlConnection(_fixture.Container.GetConnectionString());
        await connection.OpenAsync();
        await connection.ExecuteAsync(@"
            INSERT INTO [outbox].[messages_dead_letters] 
            (id, original_message_id, type, payload, correlation_id, causation_id, headers_json, created_at, dead_lettered_at, retry_count, reason, last_error)
            VALUES (@Id, @OrigId, 'test.type', NULL, NULL, NULL, NULL, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), 0, NULL, NULL)",
            new { Id = id, OrigId = origId });

        var sut = CreateSut();
        var results = await sut.GetAsync(100);
        var msg = results.FirstOrDefault(m => m.Id == id);
        msg.Should().NotBeNull();
        msg!.Reason.Should().Be("Unknown");
        System.Text.Encoding.UTF8.GetString(msg.Payload.Span).Should().Be("{}");
        System.Text.Encoding.UTF8.GetString(msg.Headers.Span).Should().Be("{}");
    }

    [Fact]
    public async Task GetAsync_Should_Filter_By_After_Date()
    {
        var sut = CreateSut();
        var msg = _autoFixture.Create<DeadLetterMessage>() with { 
            Payload = "{}"u8.ToArray(),
            Headers = "{}"u8.ToArray()
        };
        await sut.InsertAsync(msg);

        // Fetch using a date far in the future
        var results = await sut.GetAsync(after: DateTimeOffset.UtcNow.AddDays(1));
        
        // Should not contain the message we just inserted
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
            Headers = "{}"u8.ToArray()
        };
        await sut.InsertAsync(msg);

        // Purge with a time in the future so that it purges the newly inserted message
        await sut.PurgeAsync(DateTimeOffset.UtcNow.AddDays(1));

        var results = await sut.GetAsync();
        results.Should().NotContain(m => m.Id == msg.Id);
    }

    [Fact]
    public async Task Operations_WithoutTransaction_DisposeConnectionProperly()
    {
        var builder = new SqlConnectionStringBuilder(_fixture.Container.GetConnectionString())
        {
            MaxPoolSize = 2,
            ConnectTimeout = 2
        };

        var options = new OptionsMonitor<OutboxRuntimeOptions>(
            new OptionsFactory<OutboxRuntimeOptions>(Array.Empty<IConfigureOptions<OutboxRuntimeOptions>>(), Array.Empty<IPostConfigureOptions<OutboxRuntimeOptions>>()),
            Array.Empty<IOptionsChangeTokenSource<OutboxRuntimeOptions>>(),
            new OptionsCache<OutboxRuntimeOptions>());

        var sut = new SqlServerDeadLetterRepository(() => new SqlConnection(builder.ConnectionString), options);

        for (int i = 0; i < 5; i++)
        {
            var msg = _autoFixture.Create<DeadLetterMessage>() with {
                Payload = "{}"u8.ToArray(),
                Headers = "{}"u8.ToArray()
            };
            await sut.InsertAsync(msg);
            await sut.GetAsync(1);
            await sut.DeleteAsync(msg.Id);
            await sut.PurgeAsync(DateTimeOffset.UtcNow.AddYears(-1));
        }
    }
}
