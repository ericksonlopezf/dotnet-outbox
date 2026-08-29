<!-- Copyright © Erickson Lopez. MIT License. -->

# Level 11: Administration and Monitoring

This level covers the administrative tools provided by `EricksonLopez.Outbox` to monitor queue health and manage failures through the Dead Letter Queue (DLQ).

## 1. Monitoring Queue Health (`IOutboxRepository`)

For operational dashboards or health checks, it is critical to know how many messages are pending dispatch.

The `IOutboxRepository` exposes a highly optimized method that counts the pending messages:

```csharp
using EricksonLopez.Outbox.Persistence;

public interface IOutboxRepository
{
    // Returns the count of messages in Pending (0) state.
    // PostgreSQL: uses pg_class catalog estimates for large tables (> OutboxRuntimeOptions.LargeTableThreshold rows).
    ValueTask<long> GetPendingCountAsync(CancellationToken cancellationToken);
}
```

### Minimal API Health Endpoint

```csharp
app.MapGet("/health/outbox", async (IOutboxRepository repository, CancellationToken ct) =>
{
    long count = await repository.GetPendingCountAsync(ct);

    return count > 1000
        ? Results.Problem($"Outbox queue is saturated: {count} messages pending. Dispatcher may be failing.")
        : Results.Ok(new { pendingMessages = count, status = "healthy" });
});
```

*(Note: The built-in `OutboxHealthCheck` uses this internally — register it with `.AddOutbox()` on `IHealthChecksBuilder`.)*

---

## 2. Single Message Lookup (`IOutboxRepository.GetMessageAsync`)

The `IOutboxRepository` exposes two overloads for retrieving a single outbox message by its ID:

```csharp
public interface IOutboxRepository
{
    // Look up a message by ID (any state: Pending, InFlight, Dispatched, Failed).
    // Default Interface Method (DIM) — throws NotSupportedException if not overridden
    // by the storage engine implementation.
    ValueTask<OutboxMessage?> GetMessageAsync(
        Guid messageId,
        CancellationToken cancellationToken = default);

    // Overload with createdAt hint for range-partitioned tables.
    // Provides a PostgreSQL partition pruning hint: the query planner prunes all partitions
    // except the one containing the row with created_at ≈ createdAtHint.
    ValueTask<OutboxMessage?> GetMessageAsync(
        Guid messageId,
        DateTimeOffset createdAtHint,
        CancellationToken cancellationToken = default);
}
```

### Usage

```csharp
using EricksonLopez.Outbox.Persistence;

// Basic lookup:
var message = await repository.GetMessageAsync(knownMessageId, ct);
if (message is null) return Results.NotFound();

// Partition-pruning lookup (significantly faster on partitioned tables):
var message = await repository.GetMessageAsync(
    messageId: knownMessageId,
    createdAtHint: DateTimeOffset.UtcNow.AddHours(-2), // approximate creation time
    cancellationToken: ct);
```

> [!IMPORTANT]
> `GetMessageAsync` is a **Default Interface Method (DIM)**. If the storage provider you are using does not override it, calling it will throw `NotSupportedException`. `PostgreSqlOutboxRepository` provides a concrete override.

> [!TIP]
> **When to use `createdAtHint`?** In deployments using `PARTITION BY RANGE(created_at)` in PostgreSQL, providing the approximate creation timestamp allows the query planner to prune all irrelevant child partitions, resulting in a single-partition scan instead of a full sequential scan across all partitions. Even an approximate hint (within the day) is sufficient for partition pruning.

---

## 3. Managing the Dead Letter Queue (`IDeadLetterRepository`)

Messages that exceed their maximum retry limits, or encounter fatal errors (like payload size violations), are moved to the Dead Letter Queue (DLQ).

The `IDeadLetterRepository` gives you administrative control over these messages to build back-office UIs or automated replay pipelines.

```csharp
using EricksonLopez.Outbox.Persistence;

public interface IDeadLetterRepository
{
    // Fetches a paginated list of dead letters, sorted by dead_lettered_at ascending.
    ValueTask<IReadOnlyList<DeadLetterMessage>> GetAsync(
        int limit = 100,
        DateTimeOffset? after = null,         // Cursor: return records dead-lettered after this timestamp
        CancellationToken cancellationToken = default);

    // Permanently deletes a specific message from the DLQ.
    ValueTask DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    // Purges all DLQ messages older than the specified timestamp.
    ValueTask PurgeAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default);

    // Persists a new dead-lettered message (called internally by the dispatcher)
    ValueTask InsertAsync(
        DeadLetterMessage message,
        IOutboxTransactionContext? transaction = default,
        CancellationToken cancellationToken = default);
}
```

### `DeadLetterMessage` Domain Model

```csharp
// DeadLetterMessage is a record — value-based equality
public record DeadLetterMessage
{
    public Guid Id { get; init; }                   // Original outbox message ID
    public Guid OriginalMessageId { get; init; }    // Alias for Id
    public string MessageType { get; init; }         // [OutboxMessage] alias (e.g., "order-created-v1")
    public byte[] Payload { get; init; }             // Serialized message body
    public string? CorrelationId { get; init; }
    public string? CausationId { get; init; }
    public string? HeadersJson { get; init; }
    public DateTimeOffset CreatedAt { get; init; }   // When originally stored
    public DateTimeOffset DeadLetteredAt { get; init; } // When moved to DLQ
    public int RetryCount { get; init; }
    public string? Reason { get; init; }             // Short reason (max 500 chars)
    public string? LastError { get; init; }          // Sanitized exception message
}
```

### Building a Complete DLQ Management API

```csharp
using EricksonLopez.Outbox.Persistence;

// View the latest 50 failed messages:
app.MapGet("/admin/dlq", async (
    IDeadLetterRepository dlq,
    [FromQuery] DateTimeOffset? after,
    CancellationToken ct) =>
{
    var messages = await dlq.GetAsync(limit: 50, after: after, ct);
    return Results.Ok(messages);
});

// Delete a failed message after manual inspection:
app.MapDelete("/admin/dlq/{id:guid}", async (
    Guid id,
    IDeadLetterRepository dlq,
    CancellationToken ct) =>
{
    await dlq.DeleteAsync(id, ct);
    return Results.NoContent();
});

// Purge messages older than 30 days:
app.MapDelete("/admin/dlq/purge", async (
    IDeadLetterRepository dlq,
    CancellationToken ct) =>
{
    var olderThan = DateTimeOffset.UtcNow.AddDays(-30);
    await dlq.PurgeAsync(olderThan, ct);
    return Results.NoContent();
});
```

---

## 4. Soft-Delete Retention (`PurgeDispatchedMessagesAsync` + `AddOutboxCleanupService`)

By default, `DeleteOnDispatch = true`: dispatched messages are **deleted** from the outbox table immediately after successful publication.

If you need an audit trail, set `DeleteOnDispatch = false` (soft-delete mode). Dispatched messages are then retained with `state = 2 (Dispatched)`. You must then configure a retention policy to prevent unbounded table growth.

### Option A — Automatic: `AddOutboxCleanupService()`

Register the built-in background cleanup service:

```csharp
// Step 1: Configure soft-delete mode
builder.Services.AddOutbox(options =>
{
    options.ConfigureRuntimeOptions(runtime =>
    {
        runtime.DeleteOnDispatch = false; // Retain dispatched messages for audit
    });
});

// Step 2: Register the automatic cleanup worker
builder.Services.AddOutboxCleanupService(options =>
{
    options.Enabled = true;                           // Must be explicitly opted in
    options.RetentionPeriod = TimeSpan.FromDays(7);  // Delete after 7 days
    options.CleanupInterval = TimeSpan.FromHours(1); // Run every hour
    options.BatchSize = 1000;                         // Max rows per DELETE (avoids lock escalation)
});
```

### `OutboxCleanupOptions` Reference

| Property | Default | Description |
|---|---|---|
| `Enabled` | `false` | Must be `true` to activate the background cleanup service. Opt-in by design. |
| `RetentionPeriod` | `7 days` | Messages dispatched earlier than `(UtcNow - RetentionPeriod)` are purged. |
| `CleanupInterval` | `1 hour` | Interval between successive cleanup passes. |
| `BatchSize` | `1000` | Max rows per `DELETE` batch to avoid table lock escalation. |

### Option B — Manual: `IOutboxRepository.PurgeDispatchedMessagesAsync()`

For on-demand purging without the background service:

```csharp
using EricksonLopez.Outbox.Persistence;

// IOutboxRepository.PurgeDispatchedMessagesAsync(cutoff, batchSize, ct):
//   cutoff    — delete rows where ProcessedAt < cutoff
//   batchSize — max rows per DELETE (prevents lock escalation)
//   returns   — count of rows deleted
var cutoff = DateTimeOffset.UtcNow.AddDays(-7);
var purgedCount = await repository.PurgeDispatchedMessagesAsync(
    cutoff: cutoff,
    batchSize: 1000,
    cancellationToken: ct);
```

> [!WARNING]
> `PurgeDispatchedMessagesAsync` has **no effect** when `DeleteOnDispatch = true` (the default configuration), because dispatched messages are already deleted at dispatch time. Only call it in soft-delete deployments (`DeleteOnDispatch = false`).

---

## 5. `OutboxConstants` — Reserved Identifiers

```csharp
using EricksonLopez.Outbox;

// The consumer identifier used internally by the dispatcher for its own idempotency records.
// DO NOT use this in your own consumers.
string dispatcherId = OutboxConstants.DispatcherConsumerId; // = "outbox-dispatcher"
```

When calling `IInboxIdempotencyChecker.ShouldSkipAsync()` or `ShouldProcessAsync()` in your own consumers, always use a **unique, stable consumer ID** such as `"billing-service.order-created-handler"`. Reusing `OutboxConstants.DispatcherConsumerId` causes idempotency record collisions.

---

## 6. OpenTelemetry — `OutboxActivitySource`

The library emits structured distributed tracing via `OutboxActivitySource`:

```csharp
using EricksonLopez.Outbox.Diagnostics;

// The ActivitySource name — use this to subscribe to outbox spans in OpenTelemetry:
string sourceName = OutboxActivitySource.SourceName;   // = "EricksonLopez.Outbox"

// The ActivitySource instance (use for manual span creation in custom broker publishers):
ActivitySource source = OutboxActivitySource.Source;

// Register with OpenTelemetry:
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing
        .AddSource(OutboxActivitySource.SourceName)  // Subscribe to outbox spans
        .AddNpgsql()
        .AddOtlpExporter());
```

### OTel Messaging Semantic Conventions

Spans emitted by the outbox follow the [OpenTelemetry Messaging Semantic Conventions v1.26+](https://opentelemetry.io/docs/specs/semconv/messaging/):

| Tag | Value | Description |
|---|---|---|
| `messaging.system` | `"outbox"` (store) / actual broker name (dispatch) | Broker system identifier. |
| `messaging.operation.name` | `"store"` or `"publish"` | Operation type. |
| `messaging.operation.type` | `"store"` or `"publish"` | OTel structured enum. |
| `messaging.destination.name` | Message type alias | Routing key / topic name. |
| `messaging.message.id` | `{Guid}` | Unique outbox message ID. |

> [!NOTE]
> `messaging.system` defaults to `"outbox"` for the store span and for the dispatch span when no broker-specific name is set. Broker publisher implementations should override this with the actual broker name:
> ```csharp
> Activity.Current?.SetTag("messaging.system", "rabbitmq");
> ```

---

## 7. Metrics (`OutboxMetrics`)

The library emits `System.Diagnostics.Metrics` instruments:

```csharp
// Subscribe in OpenTelemetry:
builder.Services.AddOpenTelemetry()
    .WithMetrics(metrics => metrics
        .AddMeter("EricksonLopez.Outbox")
        .AddOtlpExporter());
```

### Instruments

| Meter | Instrument | Kind | Description |
|---|---|---|---|
| `EricksonLopez.Outbox` | `outbox.messages.stored` | Counter | Messages stored via `StoreAsync()`. |
| `EricksonLopez.Outbox` | `outbox.messages.dispatched` | Counter | Messages successfully published. |
| `EricksonLopez.Outbox` | `outbox.messages.failed` | Counter | Messages that failed dispatch (scheduled for retry). |
| `EricksonLopez.Outbox` | `outbox.messages.dead_lettered` | Counter | Messages moved to the DLQ. |
| `EricksonLopez.Outbox` | `outbox.messages.pending` | ObservableGauge | Current pending message count (polled every `PendingCountRefreshInterval`). |

> [!TIP]
> Set `OutboxRuntimeOptions.IncludeMessageTypeTag = false` to disable per-type metric dimensions and reduce cardinality in high-throughput scenarios.

### Grafana Dashboard

A pre-built Grafana dashboard is available at `grafana/dashboards/outbox-dashboard.json`. Import it into your Grafana instance to visualize outbox throughput, failure rates, and DLQ accumulation.

---

**Next:** In [Level 12](level-12-testing.md), you will find the complete Testing Guide with all testing utilities, patterns, and integration test setup.

Or, if you've reached the end of the Showcase:

> You've completed the `EricksonLopez.Outbox` Showcase. This covers the full public API surface from basic setup to enterprise production hardening.
