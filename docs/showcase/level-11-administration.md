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

## 4.1. `IOutboxRepository.ReclaimStaleMessagesAsync` — Crash Recovery

When the dispatcher claims messages (`InFlight` state = 1) and then crashes before calling `MarkAsDispatchedAsync`, those messages become permanently stuck in state 1. The reclaim mechanism resets them back to `Pending (0)` so they can be retried.

```csharp
using EricksonLopez.Outbox.Persistence;

// Resets any InFlight message whose updated_at < (UtcNow - staleTimeout) back to Pending(0).
// Returns the count of messages reclaimed.
int reclaimedCount = await outboxRepository.ReclaimStaleMessagesAsync(
    staleTimeout: TimeSpan.FromMinutes(5),  // Matches OutboxDispatcherOptions.ReclaimTimeout default
    cancellationToken: ct);
```

> [!NOTE]
> The `OutboxDispatcherBackgroundService` calls `ReclaimStaleMessagesAsync` automatically every `OutboxDispatcherOptions.ReclaimInterval` (default: 1 minute). You only need to call it directly for administrative tooling or in integration tests. See Showcase endpoint `POST /api/level11/outbox/reclaim-stale`.

**How crash recovery works:**

1. Dispatcher calls `FetchPendingAsync()` → atomically sets `status=1` (InFlight)
2. **Crash** — dispatcher dies before calling `MarkAsDispatchedAsync`
3. Messages remain stuck in `InFlight (1)` indefinitely
4. `ReclaimStaleMessagesAsync(staleTimeout)` resets them: `UPDATE status=0 WHERE status=1 AND updated_at < (NOW() - staleTimeout)`
5. Next `FetchPendingAsync` cycle re-claims and re-dispatches them

> [!WARNING]
> Set `staleTimeout` **greater** than the maximum time a single dispatch attempt can take (including broker connection setup and retry loops). Too short a timeout causes false-positive reclaims: reclaiming messages that are still legitimately being processed, resulting in duplicate deliveries.

---

## 4.2. `IDeadLetterRepository` — Complete Contract Reference

The `IDeadLetterRepository` interface has four methods. Sections 3 above covers `GetAsync` and `DeleteAsync`. This section documents `PurgeAsync`, `InsertAsync`, and `IsFirstPartyImplementation`:

```csharp
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Persistence;

// PurgeAsync — Bulk delete all DLQ entries older than the given timestamp.
// Unlike PurgeDispatchedMessagesAsync (which is batched), this issues a single DELETE.
// DLQ tables are typically small (only messages that exhausted all retries) so this is safe.
await dlqRepository.PurgeAsync(
    olderThan: DateTimeOffset.UtcNow.AddDays(-30),
    cancellationToken: ct);

// InsertAsync — Called by the dispatcher to persist a dead-lettered message.
// transaction: if null, the repository auto-commits on its own connection.
// This is the auto-commit requirement: the dispatcher calls InsertAsync without a transaction.
var dlq = new DeadLetterMessage(
    Id: Guid.NewGuid(),
    OriginalMessageId: originalMessage.Id,
    MessageType: originalMessage.MessageType,
    Payload: originalMessage.Payload,
    CorrelationId: originalMessage.CorrelationId,
    CausationId: originalMessage.CausationId,
    Headers: originalMessage.Headers,
    CreatedAt: originalMessage.CreatedAt,
    DeadLetteredAt: DateTimeOffset.UtcNow,
    RetryCount: originalMessage.RetryCount,
    Reason: "MaxRetryCount exceeded",
    LastError: "Connection refused after 5 attempts");

await dlqRepository.InsertAsync(dlq, transaction: null, ct);  // null = auto-commit

// IsFirstPartyImplementation — Default Interface Method (DIM), returns false by default.
// Used by OutboxStartupValidator to emit an advisory warning for third-party implementations.
bool isBuiltIn = dlqRepository.IsFirstPartyImplementation; // false if custom, true for built-in
```

### `DeadLetterMessage.FromOutboxMessage` — Factory Method

The dispatcher uses this factory to construct a `DeadLetterMessage` from a failed `OutboxMessage`:

```csharp
using EricksonLopez.Outbox;

// Factory: builds a DeadLetterMessage from a failed OutboxMessage.
// On .NET 9+, Id uses Guid.CreateVersion7() for monotonic ordering.
// On .NET 8, Id uses Guid.NewGuid().
var deadLetter = DeadLetterMessage.FromOutboxMessage(
    original: failedOutboxMessage,
    retryCount: 7,
    reason: "Broker rejected: payload schema v3 is incompatible",
    lastError: exception.ToString());  // sanitized by IErrorSanitizer before storage
```

> [!IMPORTANT]
> **Third-party `IDeadLetterRepository` implementations must handle `transaction = null`** (auto-commit mode). The dispatcher frequently calls `InsertAsync` without an active transaction (after a failed dispatch outside a user transaction boundary). Failing to handle this causes dead letters to be silently lost. The `OutboxStartupValidator` emits an advisory at startup when `IsFirstPartyImplementation` returns `false` to remind you of this requirement.

---

## 4.3. `OutboxMessage` — Optional Fields Reference

`OutboxMessage` has two optional fields that are relevant for multi-tenancy and future extensibility:

```csharp
using EricksonLopez.Outbox;

// TenantId: set via OutboxMessageBuilder.WithTenantId(tenantId)
// Stored as a dedicated indexed column — not just a header.
// The broker publisher reads it to route to a tenant-specific topic/queue.
await outbox.Publish(new OrderCreatedEvent(...))
    .WithTenantId("acme")               // → sets x-tenant-id header AND TenantId column
    .WithTransaction(tx.ToOutboxContext())
    .StoreAsync(ct);

// Extensions: IReadOnlyDictionary<string, string>?
// Currently not settable via OutboxMessageBuilder (raw constructor only).
// Reserved for future v2.0 typed routing metadata (Kafka offsets, CDC/WAL metadata).
// In v1.0: string-only values. Sufficient for string headers but precludes typed metadata.
//
// ROADMAP v2.0: Extensions will be upgraded to IReadOnlyDictionary<string, object?>
// to support non-string values. This is a binary-breaking change deferred from v1.0.
```

> [!NOTE]
> `TenantId` is documented in the multi-tenancy context in [Level 8](level-08-customization.md) and demonstrated in Showcase endpoint `GET /api/level11/outbox/message-optional-fields`. For the full multi-tenancy pattern with `ITenantBrokerRouter`, see [Level 8](level-08-customization.md) section 8i.

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
