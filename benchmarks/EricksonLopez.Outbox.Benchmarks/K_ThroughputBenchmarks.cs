// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Hosting;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace EricksonLopez.Outbox.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(RunStrategy.Throughput)]
public class K_ThroughputBenchmarks
{
    private sealed class DummyTransactionContext : IOutboxTransactionContext
    {
        public object Connection => null!;
        public object Transaction => this;
        public void Dispose() { }
    }

    private sealed class NullOutboxRepository : IOutboxRepository
    {
        public ValueTask InsertAsync(OutboxMessage message, IOutboxTransactionContext? transaction, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask InsertBatchAsync(IEnumerable<OutboxMessage> messages, IOutboxTransactionContext? transaction, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
            
        public ValueTask InsertBatchAsync(ReadOnlyMemory<OutboxMessage> messages, IOutboxTransactionContext? transaction, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
            
        public ValueTask<IReadOnlyList<OutboxMessage>> GetPendingMessagesAsync(int batchSize, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<OutboxMessage>>(Array.Empty<OutboxMessage>());
            
        public ValueTask<IReadOnlyList<OutboxMessage>> FetchPendingAsync(int batchSize, CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IReadOnlyList<OutboxMessage>>(Array.Empty<OutboxMessage>());
            
        public ValueTask MarkAsDispatchedAsync(IEnumerable<Guid> messageIds, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
            
        public ValueTask MarkAsDispatchedAsync(IReadOnlyList<OutboxMessage> messages, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
            
        public ValueTask MarkAsFailedAsync(IReadOnlyList<OutboxMessage> messages, string error, bool isDeadLetter, CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
            
        public ValueTask<int> ReclaimStaleMessagesAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(0);
            
        public ValueTask<long> GetPendingCountAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(0L);
    }

    private sealed class DummyTypeResolver : EricksonLopez.Outbox.Serialization.IOutboxMessageTypeResolver
    {
        public string GetAlias(System.Type messageType) => messageType.Name;
        public bool TryGetAlias(Type messageType, out string? alias) { alias = messageType.Name; return true; }
        public System.Type Resolve(string alias) => typeof(OrderCreatedEvent);
        public IReadOnlyDictionary<string, System.Type> GetAllMappings() => new Dictionary<string, System.Type>();
    }

    private Microsoft.Extensions.Hosting.IHost _host = null!;
    private IOutbox _outbox = null!;
    private OrderCreatedEvent _testEvent = null!;
    private DummyTransactionContext _transaction = new();

    [GlobalSetup]
    public void Setup()
    {
        _testEvent = new OrderCreatedEvent(Guid.NewGuid(), 100.5m, DateTimeOffset.UtcNow);

        var hostBuilder = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                services.AddLogging();
                services.AddSingleton<IOutboxRepository, NullOutboxRepository>();
                services.AddSingleton<EricksonLopez.Outbox.Serialization.IOutboxMessageTypeResolver, DummyTypeResolver>();
                services.AddOutbox();
                services.AddSingleton<EricksonLopez.Outbox.Serialization.IOutboxSerializer, EricksonLopez.Outbox.Serialization.NativeAotJsonSerializer>();
                services.AddSingleton<System.Text.Json.Serialization.JsonSerializerContext>(BenchmarkJsonContext.Default);
            });

        _host = hostBuilder.Build();
        _outbox = _host.Services.GetRequiredService<IOutbox>();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _host?.Dispose();
    }

    [Benchmark]
    public async Task EricksonLopezOutbox_StoreAsync_Throughput()
    {
        await _outbox.StoreAsync(_testEvent, _transaction);
    }
}




