// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox.Storage.PostgreSql;
using Xunit;

namespace EricksonLopez.Outbox.Storage.PostgreSql.Tests;

[Collection("PostgreSql")]
[Trait("Category", "Integration")]
public class PostgreSqlConcurrencyIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainerFixture _fixture;
    
    public PostgreSqlConcurrencyIntegrationTests(PostgreSqlContainerFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await PostgreSqlTestDatabase.EnsureSchemaAsync(_fixture.DataSource);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task FetchPendingAsync_WithMultipleThreads_ShouldUseSkipLockedAndNotDeadlock()
    {
        // Arrange
        int messageCount = 1000;
        int threadCount = 10;
        
        var options = new OutboxRuntimeOptions { SchemaName = "outbox", TableName = "messages", InstanceId = Guid.NewGuid().ToString() };
        var repo = new PostgreSqlOutboxRepository(_fixture.DataSource, Microsoft.Extensions.Options.Options.Create(options));
        
        // Insert 1000 messages
        var messages = Enumerable.Range(0, messageCount)
            .Select(i => PostgreSqlTestDatabase.CreateMessage(id: Guid.NewGuid()) with { Headers = System.Text.Encoding.UTF8.GetBytes("{}") })
            .ToList();
            
        await repo.InsertBulkAsync(messages, CancellationToken.None);

        var fetchedIds = new ConcurrentBag<Guid>();
        var tasks = new Task[threadCount];
        
        // Act
        for (int i = 0; i < threadCount; i++)
        {
            var workerOptions = new OutboxRuntimeOptions { SchemaName = "outbox", TableName = "messages", InstanceId = Guid.NewGuid().ToString() };
            var workerRepo = new PostgreSqlOutboxRepository(_fixture.DataSource, Microsoft.Extensions.Options.Options.Create(workerOptions));
            
            tasks[i] = Task.Run(async () => 
            {
                while (true)
                {
                    var batch = await workerRepo.FetchPendingAsync(100, CancellationToken.None);
                    if (batch.Count == 0) break;
                    
                    foreach (var msg in batch)
                    {
                        fetchedIds.Add(msg.Id);
                    }
                    
                    // Mark as dispatched so they aren't picked up again
                    await workerRepo.MarkAsDispatchedAsync(batch, CancellationToken.None);
                }
            });
        }

        await Task.WhenAll(tasks);

        // Assert
        var distinctFetched = fetchedIds.Distinct().Count();
        distinctFetched.Should().Be(messageCount);
        fetchedIds.Count.Should().Be(messageCount, "No message should be fetched twice by different threads due to SKIP LOCKED");
    }
}



