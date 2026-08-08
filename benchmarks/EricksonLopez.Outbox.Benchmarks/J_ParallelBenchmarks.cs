using System;
using System.Linq;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Testing;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Hosting;

namespace EricksonLopez.Outbox.Benchmarks;

[MemoryDiagnoser]
public class J_ParallelBenchmarks
{
    private sealed class DummyTransactionContext : IOutboxTransactionContext
    {
        public object Connection => null!;
        public object Transaction => this;
        public void Dispose() { }
    }

    private sealed class NullOutboxRepository : IOutboxRepository
    {
        public ValueTask InsertAsync(OutboxMessage message, IOutboxTransactionContext? transaction, System.Threading.CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;

        public ValueTask InsertBatchAsync(System.Collections.Generic.IEnumerable<OutboxMessage> messages, IOutboxTransactionContext? transaction, System.Threading.CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
            
        public ValueTask InsertBatchAsync(ReadOnlyMemory<OutboxMessage> messages, IOutboxTransactionContext? transaction, System.Threading.CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
            
        public ValueTask<System.Collections.Generic.IReadOnlyList<OutboxMessage>> GetPendingMessagesAsync(int batchSize, System.Threading.CancellationToken cancellationToken = default)
            => ValueTask.FromResult<System.Collections.Generic.IReadOnlyList<OutboxMessage>>(Array.Empty<OutboxMessage>());
            
        public ValueTask<System.Collections.Generic.IReadOnlyList<OutboxMessage>> FetchPendingAsync(int batchSize, System.Threading.CancellationToken cancellationToken = default)
            => ValueTask.FromResult<System.Collections.Generic.IReadOnlyList<OutboxMessage>>(Array.Empty<OutboxMessage>());
            
        public ValueTask MarkAsDispatchedAsync(System.Collections.Generic.IEnumerable<Guid> messageIds, System.Threading.CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
            
        public ValueTask MarkAsDispatchedAsync(System.Collections.Generic.IReadOnlyList<OutboxMessage> messages, System.Threading.CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
            
        public ValueTask MarkAsFailedAsync(System.Collections.Generic.IReadOnlyList<OutboxMessage> messages, string error, bool isDeadLetter, System.Threading.CancellationToken cancellationToken = default)
            => ValueTask.CompletedTask;
            
        public ValueTask<int> ReclaimStaleMessagesAsync(TimeSpan timeout, System.Threading.CancellationToken cancellationToken = default)
            => ValueTask.FromResult(0);
            
        public ValueTask<long> GetPendingCountAsync(System.Threading.CancellationToken cancellationToken = default)
            => ValueTask.FromResult(0L);
    }

    private sealed class DummyTypeResolver : EricksonLopez.Outbox.Serialization.IOutboxMessageTypeResolver
    {
        public string GetAlias(System.Type messageType) => messageType.Name;
        public bool TryGetAlias(Type messageType, out string? alias) { alias = messageType.Name; return true; }
        public System.Type Resolve(string alias) => typeof(OrderCreatedEvent);
        public System.Collections.Generic.IReadOnlyDictionary<string, System.Type> GetAllMappings() => new System.Collections.Generic.Dictionary<string, System.Type>();
    }

    private Microsoft.Extensions.Hosting.IHost _host = null!;
    private IOutbox _outbox = null!;
    private OrderCreatedEvent _testEvent = null!;
    private DummyTransactionContext _transaction = new();

    [Params(1, 4, 16, 64)]
    public int ThreadCount { get; set; }

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
    public async Task EricksonLopezOutbox_StoreAsync_Parallel()
    {
        var tasks = new Task[ThreadCount];
        for (int i = 0; i < ThreadCount; i++)
        {
            tasks[i] = Task.Run(() => _outbox.StoreAsync(_testEvent, _transaction));
        }
        await Task.WhenAll(tasks);
    }
}
