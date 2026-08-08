using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using DotNetCore.CAP;
using DotNetCore.CAP.Internal;
using DotNetCore.CAP.Messages;
using DotNetCore.CAP.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using EricksonLopez.Outbox;
using NServiceBus;
using EricksonLopez.Outbox.Testing;
using EricksonLopez.Outbox.Persistence;

using EricksonLopez.Outbox.Hosting;
using Savorboard.CAP.InMemoryMessageQueue;

namespace EricksonLopez.Outbox.Benchmarks;

[MemoryDiagnoser]
[BenchmarkDotNet.Attributes.RankColumn]
// B-01 AUDIT FIX: Benchmark Methodology Disclaimer
//
// TIER 1 BENCHMARK (Framework Overhead Only — No Database I/O)
// ============================================================
// This benchmark measures the per-call overhead of the outbox FRAMEWORK layer:
// serialization, type resolution, DI resolution, and allocation profile.
//
// It does NOT measure end-to-end outbox performance because:
//   - EricksonLopez.Outbox uses NullOutboxRepository (InsertAsync = ValueTask.CompletedTask)
//   - CAP uses NullCapDataStorage (StoreMessageAsync = Task.FromResult(fakeMsg))
//   - NServiceBus uses LearningTransport (writes to a temp directory on disk)
//
// VALID CLAIMS FROM THIS BENCHMARK:
//   - Framework serialization overhead (payload encoding, type resolution)
//   - Allocation profile per message (Gen0, Gen1, alloc bytes)
//   - DI/middleware pipeline overhead per call
//   - Zero-reflection vs reflection-based message handling
//
// INVALID CLAIMS FROM THIS BENCHMARK:
//   - "EricksonLopez.Outbox is X% faster than CAP/NServiceBus" at full outbox performance
//   - Any throughput numbers that imply real-world DB write performance
//
// TIER 2 BENCHMARK (Full Outbox — Real DB Required):
//   To make valid end-to-end performance claims, run against real backends:
//   - EricksonLopez.Outbox: PostgreSqlOutboxRepository with real NpgsqlDataSource
//   - CAP: PostgreSQL or SQL Server backed IDataStorage
//   - NServiceBus: SQL Server or PostgreSQL persistence
//   Use Testcontainers for reproducible, isolated DB instances.
//   See: H_SqlFetchBenchmarks.cs for the database I/O benchmark (EricksonLopez.Outbox only).
//
// HARDWARE NOTE:
//   Results are machine-specific. Include BenchmarkDotNet's full environment table
//   when reporting results externally. Do not cite numbers without hardware context.
public class I_CompetitorBenchmarks
{
    private sealed class NullCapDataStorage : IDataStorage
    {
        public Task<bool> AcquireLockAsync(string key, TimeSpan ttl, string instance, CancellationToken token = default) => Task.FromResult(true);
        public Task ReleaseLockAsync(string key, string instance, CancellationToken token = default) => Task.CompletedTask;
        public Task RenewLockAsync(string key, TimeSpan ttl, string instance, CancellationToken token = default) => Task.CompletedTask;

        public Task ChangePublishStateAsync(MediumMessage message, StatusName state, object? transaction = null) => Task.CompletedTask;
        public Task ChangePublishStateToDelayedAsync(string[] messageIds) => Task.CompletedTask;
        public Task ChangeReceiveStateAsync(MediumMessage message, StatusName state) => Task.CompletedTask;

        public Task<MediumMessage> StoreMessageAsync(string name, Message content, object? transaction = null) 
            => Task.FromResult(new MediumMessage { DbId = "1", Content = "{}", Added = DateTime.Now, Origin = content });
        public Task StoreReceivedExceptionMessageAsync(string name, string group, string content) => Task.CompletedTask;
        public Task<MediumMessage> StoreReceivedMessageAsync(string name, string group, Message content) 
            => Task.FromResult(new MediumMessage { DbId = "1", Content = "{}", Added = DateTime.Now, Origin = content });

        public Task<int> DeleteExpiresAsync(string table, DateTime timeout, int batchCount = 1000, CancellationToken token = default) => Task.FromResult(0);
        public Task<IEnumerable<MediumMessage>> GetPublishedMessagesOfNeedRetry(TimeSpan timeout) => Task.FromResult(System.Linq.Enumerable.Empty<MediumMessage>());
        public Task<IEnumerable<MediumMessage>> GetReceivedMessagesOfNeedRetry(TimeSpan timeout) => Task.FromResult(System.Linq.Enumerable.Empty<MediumMessage>());
        public Task ScheduleMessagesOfDelayedAsync(Func<object, IEnumerable<MediumMessage>, Task> scheduleTask, CancellationToken token = default) => Task.CompletedTask;
        
        public DotNetCore.CAP.Monitoring.IMonitoringApi GetMonitoringApi() => null!;
    }

    private sealed class NsTestEvent : IEvent
    {
        public decimal Amount { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    private DotNetCore.CAP.ICapPublisher _capPublisher = null!;
#pragma warning disable CS0618
    private IEndpointInstance _nsEndpoint = null!;
#pragma warning restore CS0618
    private NsTestEvent _nsTestEvent = null!;
    private OrderCreatedEvent _testEvent = null!;
    
    // --- Outbox Setup ---
    private IOutbox _outbox = null!;
    private DummyTransactionContext _transaction = new();
    
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

    private Microsoft.Extensions.Hosting.IHost _host = null!;

    [GlobalSetup]
    public void Setup()
    {
        _testEvent = new OrderCreatedEvent(Guid.NewGuid(), 100.5m, DateTimeOffset.UtcNow);
        _nsTestEvent = new NsTestEvent { Amount = 100.5m, CreatedAt = DateTimeOffset.UtcNow };

        var hostBuilder = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                services.AddLogging();
                
                // Outbox baseline setup
                services.AddSingleton<EricksonLopez.Outbox.Persistence.IOutboxRepository, NullOutboxRepository>();
                services.AddSingleton<EricksonLopez.Outbox.Serialization.IOutboxMessageTypeResolver, DummyTypeResolver>();
                services.AddOutbox();
                services.AddSingleton<EricksonLopez.Outbox.Serialization.IOutboxSerializer, EricksonLopez.Outbox.Serialization.NativeAotJsonSerializer>();
                services.AddSingleton<System.Text.Json.Serialization.JsonSerializerContext>(BenchmarkJsonContext.Default);
                
                // CAP setup
                services.AddCap(x =>
                {
                    x.UseInMemoryStorage();
                    x.UseInMemoryMessageQueue();
                });
                services.AddSingleton<IDataStorage, NullCapDataStorage>();
            });

        _host = hostBuilder.Build();
        _host.Start();

        // Resolve CAP
        _capPublisher = _host.Services.GetRequiredService<DotNetCore.CAP.ICapPublisher>();
        
        _outbox = _host.Services.GetRequiredService<IOutbox>();
        
        // NServiceBus setup
        var tempPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "NsBench");
        if (System.IO.Directory.Exists(tempPath)) System.IO.Directory.Delete(tempPath, true);
        
        var endpointConfiguration = new EndpointConfiguration("EricksonLopez.Outbox.Benchmarks");
        var transport = new LearningTransport { StorageDirectory = tempPath };
        endpointConfiguration.UseTransport(transport);
        endpointConfiguration.UsePersistence<LearningPersistence>();
        endpointConfiguration.UseSerialization<SystemJsonSerializer>();
        endpointConfiguration.EnableOutbox();
        endpointConfiguration.SendOnly();
        
#pragma warning disable CS0618
        _nsEndpoint = Endpoint.Start(endpointConfiguration).GetAwaiter().GetResult();
#pragma warning restore CS0618
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        if (_nsEndpoint != null)
        {
#pragma warning disable CS0618
            await _nsEndpoint.Stop();
#pragma warning restore CS0618
        }
        
        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
    }

    // ----- TIER 1 BENCHMARKS: Framework overhead with null/in-memory storage -----
    // These measure serialization, DI, and middleware pipeline cost only.
    // No real database I/O is performed. See class-level disclaimer above.

    /// <summary>
    /// CAP framework overhead: serialization + null data storage.
    /// Storage: NullCapDataStorage (StoreMessageAsync returns a fake MediumMessage immediately).
    /// </summary>
    [Benchmark]
    public async Task CAP_StoreAsync()
    {
        await _capPublisher.PublishAsync("order.created", _testEvent);
    }
    
    /// <summary>
    /// NServiceBus framework overhead: serialization + LearningTransport (writes to temp directory).
    /// NOTE: Unlike the other two benchmarks, NServiceBus DOES perform file system I/O via
    /// LearningTransport. Results may be significantly affected by disk speed and OS buffering.
    /// Compare with caution.
    /// </summary>
    [Benchmark]
    public async Task NServiceBus_StoreAsync()
    {
        await _nsEndpoint.Publish(_nsTestEvent);
    }
    
    /// <summary>
    /// EricksonLopez.Outbox framework overhead: zero-reflection serialization + null storage.
    /// Storage: NullOutboxRepository (InsertAsync = ValueTask.CompletedTask).
    /// Baseline = true: other benchmarks are shown as ratio vs this one.
    /// </summary>
    [Benchmark(Baseline = true)]
    public async Task EricksonLopezOutbox_StoreAsync()
    {
        await _outbox.StoreAsync(_testEvent, _transaction);
    }
}
