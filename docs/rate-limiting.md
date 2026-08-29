<!-- Copyright © Erickson Lopez. MIT License. -->

# Rate Limiting & Throughput Management Guide

`EricksonLopez.Outbox` provides independent, fine-grained rate limiting controls on both sides of the transactional outbox pipeline:

1. **Store Path Rate Limiting**: Controls the ingestion rate of messages entering the outbox to protect database IOPS from sudden traffic spikes.
2. **Dispatch Path Rate Limiting**: Controls the batch throughput sent to external message brokers (RabbitMQ, Kafka, Azure Service Bus, etc.) to prevent broker throttling and backpressure collapse.

---

## 1. Store Path Rate Limiting (`MaxStoreRatePerSecond`)

When sudden traffic spikes hit your application (e.g., flash sales, mass data imports), thousands of concurrent requests might attempt to insert outbox messages simultaneously.

You can configure `MaxStoreRatePerSecond` in `OutboxRuntimeOptions` / `OutboxOptions`:

```csharp
services.AddOutboxServices(builder =>
{
    builder.ConfigureRuntime(options =>
    {
        // Limit outbox writes to 5,000 messages/sec per instance
        options.MaxStoreRatePerSecond = 5000;
        
        // Payload size guard (default: 1 MB)
        options.MaxPayloadSizeInBytes = 1024 * 1024;
        
        // Headers size guard (default: 64 KB)
        options.MaxHeaderSizeInBytes = 64 * 1024;
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
services.AddOutboxServices(builder =>
{
    builder.ConfigureDispatcher(options =>
    {
        // Max 50 batches dispatched per second
        options.MaxBatchesPerSecond = 50;
        
        // Batch size per query
        options.BatchSize = 100;
        
        // Max concurrency
        options.MaxDegreeOfParallelism = Environment.ProcessorCount;
        
        // Polling interval when idle
        options.PollingInterval = TimeSpan.FromMilliseconds(500);
    });
});
```

---

## 3. Adaptive Polling Integration

The built-in `AdaptivePoller` dynamically reduces polling latency down to sub-millisecond intervals during active message flow, and gracefully backs off to `PollingInterval` during idle periods. When using PostgreSQL with `PostgresNotificationListener`, polling sleep is eliminated entirely in favor of instant push notifications via `LISTEN/NOTIFY`.
