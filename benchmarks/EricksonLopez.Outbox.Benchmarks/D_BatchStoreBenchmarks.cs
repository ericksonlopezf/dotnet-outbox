using System;
using BenchmarkDotNet.Attributes;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Testing;
using Microsoft.Extensions.DependencyInjection;
using EricksonLopez.Outbox.Hosting;
using EricksonLopez.Outbox.Persistence;

namespace EricksonLopez.Outbox.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(iterationCount: 15, warmupCount: 10)]
[HideColumns("Error", "StdDev", "Median", "RatioSD")]
public class D_BatchStoreBenchmarks
{
    private IOutbox _store = null!;
    private OrderCreatedEvent[] _batchEvents = null!;
    private IServiceProvider _serviceProvider = null!;
    private Microsoft.Extensions.Hosting.IHost _host = null!;

    [Params(1, 100, 1000, 10000)]
    public int BatchSize { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _transaction = new DummyTransactionContext();
        _batchEvents = new OrderCreatedEvent[BatchSize];
        for (int i = 0; i < BatchSize; i++)
            _batchEvents[i] = new OrderCreatedEvent(Guid.NewGuid(), i * 1.5m, DateTimeOffset.UtcNow);

        var hostBuilder = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                // Register a /dev/null repository so we can run millions of invocations without blowing up memory
                services.AddSingleton<EricksonLopez.Outbox.Persistence.IOutboxRepository, NullOutboxRepository>();
                services.AddSingleton<EricksonLopez.Outbox.Serialization.IOutboxMessageTypeResolver, DummyTypeResolver>();
                services.AddOutbox();
                services.AddSingleton<EricksonLopez.Outbox.Serialization.IOutboxSerializer, EricksonLopez.Outbox.Serialization.NativeAotJsonSerializer>();
                services.AddSingleton<System.Text.Json.Serialization.JsonSerializerContext>(BenchmarkJsonContext.Default);
            });

        _host = hostBuilder.Build();
        _host.StartAsync().GetAwaiter().GetResult();
        _serviceProvider = _host.Services;
        _store = _serviceProvider.GetRequiredService<IOutbox>();
    }

    [GlobalCleanup]
    public async System.Threading.Tasks.Task Cleanup()
    {
        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
    }

    private IOutboxTransactionContext _transaction = null!;

    [Benchmark]
    public async System.Threading.Tasks.ValueTask EricksonLopezOutbox_StoreAsync_Batch()
    {
        await _store.StoreAsync(new System.ReadOnlyMemory<OrderCreatedEvent>(_batchEvents), _transaction);
    }
    
    private sealed class DummyTransactionContext : IOutboxTransactionContext
    {
        public object Transaction => this;
        public object? Connection => null;
    }
    
    private sealed class NullOutboxRepository : EricksonLopez.Outbox.Persistence.IOutboxRepository
    {
        public System.Threading.Tasks.ValueTask InsertAsync(EricksonLopez.Outbox.OutboxMessage message, EricksonLopez.Outbox.Persistence.IOutboxTransactionContext? transaction, System.Threading.CancellationToken cancellationToken = default)
            => System.Threading.Tasks.ValueTask.CompletedTask;

        public System.Threading.Tasks.ValueTask InsertBatchAsync(System.ReadOnlyMemory<EricksonLopez.Outbox.OutboxMessage> messages, EricksonLopez.Outbox.Persistence.IOutboxTransactionContext? transaction, System.Threading.CancellationToken cancellationToken = default)
            => System.Threading.Tasks.ValueTask.CompletedTask;

        public System.Threading.Tasks.ValueTask<System.Collections.Generic.IReadOnlyList<EricksonLopez.Outbox.OutboxMessage>> FetchPendingAsync(int maxBatchSize, System.Threading.CancellationToken cancellationToken = default)
            => System.Threading.Tasks.ValueTask.FromResult<System.Collections.Generic.IReadOnlyList<EricksonLopez.Outbox.OutboxMessage>>(System.Array.Empty<EricksonLopez.Outbox.OutboxMessage>());

        public System.Threading.Tasks.ValueTask MarkAsDispatchedAsync(System.Collections.Generic.IReadOnlyList<EricksonLopez.Outbox.OutboxMessage> messages, System.Threading.CancellationToken cancellationToken = default)
            => System.Threading.Tasks.ValueTask.CompletedTask;

        public System.Threading.Tasks.ValueTask MarkAsFailedAsync(System.Collections.Generic.IReadOnlyList<EricksonLopez.Outbox.OutboxMessage> messages, string error, bool isDeadLetter = false, System.Threading.CancellationToken cancellationToken = default)
            => System.Threading.Tasks.ValueTask.CompletedTask;

        public System.Threading.Tasks.ValueTask<int> ReclaimStaleMessagesAsync(System.TimeSpan staleTimeout, System.Threading.CancellationToken cancellationToken = default)
            => System.Threading.Tasks.ValueTask.FromResult(0);

        public System.Threading.Tasks.ValueTask<long> GetPendingCountAsync(System.Threading.CancellationToken cancellationToken = default)
            => System.Threading.Tasks.ValueTask.FromResult(0L);
    }
    
    private sealed class DummyTypeResolver : EricksonLopez.Outbox.Serialization.IOutboxMessageTypeResolver
    {
        public string GetAlias(System.Type messageType) => messageType.Name;
        public bool TryGetAlias(System.Type messageType, out string alias) { alias = messageType.Name; return true; }
        public System.Type GetType(string alias) => typeof(OrderCreatedEvent);
        public bool TryGetType(string alias, out System.Type type) { type = typeof(OrderCreatedEvent); return true; }
        public System.Type Resolve(string alias) => typeof(OrderCreatedEvent);
        public System.Collections.Generic.IReadOnlyDictionary<string, System.Type> GetAllMappings() => new System.Collections.Generic.Dictionary<string, System.Type>();
    }
}
