<!-- Copyright © Erickson Lopez. MIT License. -->

# Rate Limiting & Throughput Management Guide

`EricksonLopez.Outbox` provides throughput controls and protection guards across the transactional outbox pipeline:

1. **Store Path Protection**: Size guards and payload limits that protect the database and network from oversized records during storage.
2. **Dispatch Path Rate Limiting (`MaxBatchesPerSecond`)**: Dynamic rate limiting on batch dispatching to prevent message broker throttling and backpressure collapse.

---

## 1. Store Path Protection & Options

When sudden traffic spikes hit your application (e.g., flash sales, mass data imports), thousands of concurrent requests might attempt to insert outbox messages simultaneously.

You configure storage runtime options via `services.AddOutbox`:

```csharp
builder.Services.AddOutbox(options =>
{
    // Payload size guard (default: 1 MB)
    options.ConfigureRuntimeOptions(runtime =>
    {
        runtime.MaxPayloadSizeInBytes = 1024 * 1024;
        
        // Headers size guard (default: 64 KB)
        runtime.MaxHeaderSizeInBytes = 64 * 1024;
        
        // Maximum message age (default: 30 days)
        runtime.MaxMessageAge = TimeSpan.FromDays(30);

        // MaxStoreRatePerSecond is reserved for client-side throttling (default: 0 = unbounded)
        runtime.MaxStoreRatePerSecond = 0;
    });
});
```

### Protection Guards & Exceptions
- `OutboxPayloadTooLargeException`: Thrown if a serialized message exceeds `MaxPayloadSizeInBytes`.
- `OutboxHeadersTooLargeException`: Thrown if serialized metadata headers exceed `MaxHeaderSizeInBytes`.
- `ArgumentOutOfRangeException` (Dead Zone Guard): Thrown if `deliverAt` is set farther in the future than `MaxMessageAge`.

---

## 2. Dispatch Path Rate Limiting (`MaxBatchesPerSecond`)

To prevent overwhelming message brokers with excessive batch dispatches, configure `MaxBatchesPerSecond` in `OutboxDispatcherOptions`:

```csharp
builder.Services.AddOutboxDispatcher(options =>
{
    // Max 50 batches dispatched per second (enforced by AdaptivePoller)
    options.MaxBatchesPerSecond = 50;
    
    // Batch size per query (default: 100)
    options.BatchSize = 100;
    
    // Max concurrency draining the dispatch channel (default: min(ProcessorCount, 8))
    options.MaxDegreeOfParallelism = Environment.ProcessorCount;
    
    // Polling interval when idle (default: 500ms)
    options.PollingInterval = TimeSpan.FromMilliseconds(500);
});
```

---

## 3. Adaptive Polling Integration

The built-in `AdaptivePoller` dynamically reduces polling latency down to sub-millisecond intervals during active message flow, enforcing `MaxBatchesPerSecond` delay between cycles to avoid CPU spinning, and gracefully backs off to `PollingInterval` during idle periods. When using PostgreSQL with `PostgreSqlNotificationListener`, polling sleep is eliminated entirely in favor of instant push notifications via `LISTEN/NOTIFY`.

