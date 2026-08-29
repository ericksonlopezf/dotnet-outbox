// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox.Storage.PostgreSql;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Storage.PostgreSql.Tests;

[Collection("PostgreSql")]
[Trait("Category", "Integration")]
public class PostgreSqlIdempotencyIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainerFixture _fixture;
    
    public PostgreSqlIdempotencyIntegrationTests(PostgreSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await PostgreSqlTestDatabase.EnsureSchemaAsync(_fixture.DataSource);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task TryInsertAsync_UnderConcurrency_ShouldReturnTrueOnlyOnce()
    {
        // Arrange
        var messageId = Guid.NewGuid().ToString();
        var consumerId = "MyTestConsumer";
        var optionsMonitor = NSubstitute.Substitute.For<Microsoft.Extensions.Options.IOptionsMonitor<EricksonLopez.Outbox.OutboxRuntimeOptions>>();
        optionsMonitor.CurrentValue.Returns(new EricksonLopez.Outbox.OutboxRuntimeOptions { SchemaName = "outbox", TableName = "messages" });
        var repo = new PostgreSqlIdempotencyRepository(_fixture.DataSource, optionsMonitor);
        
        var record = new EricksonLopez.Outbox.IdempotencyRecord(messageId, consumerId, DateTimeOffset.UtcNow);

        // Act
        // Simulate 10 concurrent consumers receiving the identical message simultaneously
        int successCount = 0;
        int threadCount = 10;
        var tasks = new Task[threadCount];

        for (int i = 0; i < threadCount; i++)
        {
            tasks[i] = Task.Run(async () =>
            {
                try
                {
                    bool inserted = await repo.TryInsertAsync(record, null, CancellationToken.None);
                    if (inserted)
                    {
                        Interlocked.Increment(ref successCount);
                    }
                }
                catch (Exception)
                {
                    // In a highly concurrent insert, Postgres might throw Unique Constraint Violation 
                    // if it doesn't use ON CONFLICT DO NOTHING (PostgreSqlIdempotencyRepository uses ON CONFLICT DO NOTHING).
                    // If it throws, that means it didn't insert.
                }
            });
        }

        await Task.WhenAll(tasks);

        // Assert
        successCount.Should().Be(1, "Only one thread should successfully insert the idempotency record, proving At-Most-Once delivery protection");
    }
}



