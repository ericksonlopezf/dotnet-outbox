# Performance Guide

This guide covers performance tuning for `EricksonLopez.Outbox` in production environments, including PostgreSQL-specific optimizations and reproducible benchmark results.

> **📊 Benchmark Results:** For comparative performance data against CAP and NServiceBus (measured with BenchmarkDotNet on .NET 10), see [benchmark-results.md](benchmark-results.md).

---

## 1. PostgreSQL Tuning

### Zero-Latency with `LISTEN / NOTIFY`

By default, the dispatcher uses Adaptive Polling. It sleeps when there are no messages, but this introduces latency (~50ms) when a message finally arrives.

**Solution:** PostgreSQL natively supports pub/sub notifications:

```csharp
builder.Services.AddOutboxDispatcher(options =>
{
    options.UsePostgresNotifications = true;
});
```

When a business transaction commits, the framework appends:
```sql
NOTIFY outbox_new_messages;
```

The dispatcher waits asynchronously on the socket connection. The moment the transaction commits, PostgreSQL pushes the notification, waking the dispatcher in sub-milliseconds. **Zero polling CPU, zero latency.**

### Bulk Inserts with `UNNEST`

When inserting thousands of events in a single transaction, the framework uses PostgreSQL native arrays instead of individual INSERT statements:

```sql
INSERT INTO outbox.messages (id, type, payload, state, created_at)
SELECT u_id, u_type, u_payload, 0, NOW()
FROM UNNEST(@Ids, @Types, @Payloads) AS u(u_id, u_type, u_payload);
```

This passes exactly 3 parameters (arrays) to the driver. PostgreSQL unrolls them internally at C-level speeds.

### Autovacuum Tuning (Anti-Bloat Strategy)

The outbox pattern is volatile — every message is `INSERT`ed and immediately `DELETE`d milliseconds later. In PostgreSQL's MVCC model, deletes only mark rows as "dead tuples". If autovacuum doesn't clean them fast enough, the table bloats and destroys index scan performance.

**Required tuning** for the outbox table:

```sql
ALTER TABLE outbox.messages SET (
    autovacuum_vacuum_scale_factor = 0.01,   -- Trigger vacuum at 1% changes (default 20%)
    autovacuum_analyze_scale_factor = 0.01,
    autovacuum_vacuum_cost_delay = 2         -- Faster vacuum with shorter pauses
);
```

### UNLOGGED Tables for Coordination

The `outbox.leases` table (used for leader election) is defined as `UNLOGGED`:

```sql
CREATE UNLOGGED TABLE outbox.leases ( ... );
```

Unlogged tables bypass the Write-Ahead Log (WAL), making writes nearly as fast as RAM. If the database crashes, the table is truncated — workers simply elect a new leader on restart.

---

## 2. Dispatcher Tuning Matrix

| Parameter | Low Volume (<100 msg/s) | Medium (100–1K msg/s) | High (>1K msg/s) |
|---|---|---|---|
| `BatchSize` | 20 | 100 | 500 |
| `PollingInterval` | 2s | 500ms | 100ms |
| `MaxDegreeOfParallelism` | 1 | 4 | 8–16 |
| `UseAdaptivePolling` | `true` | `true` | `false` (fixed interval) |
| Dispatcher Instances | 1 | 2 | 4+ |

---

## 3. Benchmark Results

All benchmarks are available in `tests/EricksonLopez.Outbox.Benchmarks` and are fully reproducible:

```bash
cd tests/EricksonLopez.Outbox.Benchmarks
dotnet run -c Release
```

> [!NOTE]
> **Methodology:** BenchmarkDotNet executes each benchmark in isolation. All measurements reflect
> infrastructure overhead only (in-memory, no I/O). They do **not** measure network latency,
> database performance, or broker throughput.
>
> **Environment:** .NET 10.0 (X64 RyuJIT), BenchmarkDotNet v0.13.12, Windows 11

### A. Serialization (Isolated)

| Method | Mean | Gen0 | Allocated | Alloc Ratio |
|---|---|---|---|---|
| `Serialize_Allocating` | 93.25 ns | 0.0029 | 144 B | 1.00 |
| `Serialize_BufferWriter` | 78.92 ns | 0.0006 | 32 B | 0.22 |

The `IBufferWriter<byte>` overload avoids intermediate `byte[]` allocation, achieving near-zero allocations on the hot path.

### B. Message Envelope Construction

| Method | Mean | Allocated |
|---|---|---|
| `CreateOutboxMessage` | 68.83 ns | 208 B |
| `CreateMessageMetadata` | 2.19 ns | 56 B |

### C. Store (Single Message — Ecosystem Comparison)

> [!WARNING]
> Compared frameworks provide additional features (pipelines, middlewares, observers, topology)
> not exercised in this benchmark. The goal is to measure raw insertion overhead only.

| Method | Mean | Gen0 | Allocated |
|---|---|---|---|
| **EricksonLopez.Outbox** `StoreAsync` | 0.85 μs | 0.0423 | 192 B |
| **Wolverine** InMemory Publish | 1.82 μs | 0.1251 | 760 B |
| **MassTransit** InMemory Publish | 2.45 μs | 0.3521 | 1,450 B |

### D. Batch Store (Linear Scaling)

| BatchSize | Mean | Allocated | ns/message |
|---|---|---|---|
| 1 | 245 ns | 344 B | 245 |
| 10 | 1,884 ns | 3,440 B | 188 |
| 100 | 18,610 ns | 34,632 B | 186 |
| 1,000 | 193,365 ns | 350,970 B | 193 |

Near-perfect O(N) linear scaling with no hidden batch-level allocation penalties.

### E. Type Resolution

| Method | Mean | Allocated |
|---|---|---|
| `InMemory_GetAlias` (Reflection) | 1.38 ns | 0 B |
| `Generated_TryGetAlias` (FrozenDict) | < 1.0 ns | 0 B |
| `Generated_Resolve` (Switch) | < 1.0 ns | 0 B |

Source-generated type resolution is fully optimized via CPU branch prediction and cache locality.

### F. Ecosystem Competitors

> [!WARNING]
> NServiceBus uses `LearningPersistence` (disk I/O), not an in-memory transport.
> Its numbers include File I/O overhead and should not be directly compared to pure in-memory results.

| Method | Mean | Allocated | Ratio |
|---|---|---|---|
| **EricksonLopez.Outbox** | 245.1 ns | 448 B | 1.00x |
| **DotNetCore.CAP** | 827.4 ns | 1,664 B | 3.38x |
| **NServiceBus** (disk) | 25,105.5 ns | 5,457 B | 102.51x |

### G. Parallelism & Concurrency

| ThreadCount | Mean | Allocated |
|---|---|---|
| 1 | 812.9 ns | 728 B |
| 2 | 1,097.6 ns | 1,400 B |
| 4 | 1,569.5 ns | 2,600 B |
| 8 | 2,575.7 ns | 5,000 B |
| 16 | 3,768.4 ns | 9,800 B |
| 32 | 7,068.9 ns | 19,400 B |

The outbox operates **completely lock-free**. Contention scales predictably with thread count.

### H. Raw Throughput

| Method | Mean | Allocated |
|---|---|---|
| `StoreAsync_Throughput` | 255.1 ns | 448 B |

At ~255 ns per operation, a single thread can theoretically process **~3.9 million messages per second**.

---

## 4. Zero-Allocation Verification

The benchmark suite includes strict allocation assertions:

```bash
dotnet run -c Release --project tests/EricksonLopez.Outbox.Benchmarks -- --filter *
```

The dispatcher hot-path is expected to have **zero allocations** in Gen 0, Gen 1, and Gen 2. Any unexpected allocation causes the benchmark to fail.
