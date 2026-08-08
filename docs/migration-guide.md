# Migration Guide

This guide describes any breaking changes introduced across versions of the library
and how to adapt your code.

> [!NOTE]
> This is the **first public release** of `EricksonLopez.Outbox` (**v1.0.0**).
> There are no prior public versions to migrate from.
> This document will be updated when future versions introduce breaking changes.

---

## v1.0.0 — Initial Release

`EricksonLopez.Outbox` v1.0.0 is the initial public release of the library. The
full public API surface is documented in the [API Reference](api-reference.md).

If you were using an **internal pre-release build** (e.g., a local `nuget pack`
from the repository before the official publish), the following changes were made
during the pre-release stabilization period:

### 1. Storage Repository Rename (Dapper Removal)

During internal development, an early proof-of-concept used Dapper for storage.
The final release uses raw ADO.NET exclusively (see [ADR-010](adr/010-remove_dapper_raw_adonet.md)).

**How to migrate** (from an internal pre-release build only):

If you were using `SqlServerDapperOutboxRepository`, it is now simply
`SqlServerOutboxRepository` and depends on the official Microsoft driver
(`Microsoft.Data.SqlClient`) without requiring Dapper — gaining ~15% insertion speed.

```diff
- using EricksonLopez.Outbox.Dapper;
+ using EricksonLopez.Outbox.Storage.SqlServer;

- services.AddScoped<IOutboxRepository, SqlServerDapperOutboxRepository>();
+ services.AddScoped<IOutboxRepository, SqlServerOutboxRepository>();
```

### 2. `IBrokerPublisher` Signature Stabilization

During internal development, the `PublishAsync` method received a raw `OutboxMessage`
and a byte array. The final v1.0.0 API uses strongly-typed `DispatchContext`:

```diff
- public Task PublishAsync(OutboxMessage message, CancellationToken ct)
+ public ValueTask<DispatchResult> PublishRawAsync(
+     OutboxMessage message,
+     MessageMetadata metadata,
+     DispatchContext context)
```

If you develop a generic publisher that ignores types and only sends raw JSON,
implement only `PublishRawAsync` on `IBrokerPublisher`.

For strongly-typed publishing, implement `ITypedBrokerPublisher`:
```csharp
public ValueTask<DispatchResult> PublishAsync<T>(
    MessageEnvelope<T> envelope, DispatchContext context) where T : notnull;
```

---

## Known Behavior Notes (v1.0.0)

### DLQ Insert Failure Behavior

If the INSERT into the Dead Letter Queue table fails (e.g., the DLQ database is down),
the message is **always** promoted to `state=4` (DeadLettered) in the outbox table,
regardless of whether the DLQ INSERT succeeds. If the INSERT fails, the operator is
alerted with an Error-level log (`DlqInsertFailed`, EventId 10003) containing the
`messageId` for manual recovery.

**If you need a message to remain retryable when the DLQ is unavailable**, implement
a custom `IDeadLetterRepository` with its own retry/fallback logic:

```csharp
public class ResilientDlqRepository : IDeadLetterRepository
{
    public async ValueTask InsertAsync(DeadLetterMessage message,
        IOutboxTransactionContext? transaction, CancellationToken ct)
    {
        // Implement your own retry/fallback logic here.
        // If this method throws, the outbox message will still be
        // marked as DeadLettered (state=4) to prevent infinite loops.
        await _primaryDlq.InsertAsync(message, transaction, ct);
    }
}
```

### EventId 10011 (`InvalidDispatchResultDetected`)

Be aware of EventId 10011 if your system filters logs by EventId. This log message
appears when `IBrokerPublisher.PublishRawAsync` returns `default(DispatchResult)`,
which indicates a bug in the publisher implementation.
