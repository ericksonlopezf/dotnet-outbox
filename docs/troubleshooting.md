# Troubleshooting & FAQ

## Frequently Asked Questions

### Why can't I just use `SaveChanges()` and then publish to the broker?

This scenario, known as the **Dual-Write Problem**, is dangerous. If your application crashes after `SaveChanges()` completes but before the broker publish call, you'll have permanent inconsistency: the database has the new state, but the rest of your microservices never learned about it. The **Transactional Outbox** solves this by guaranteeing both writes occur atomically within the same database transaction (ACID).

### How does the Inbox pattern solve the "At-Least-Once" problem?

Message brokers (Kafka, RabbitMQ) promise at-least-once delivery, which means duplicates are inevitable during network partitions. If a consumer receives `OrderCreated(Id: 10)` twice, it could charge the customer twice.

`IInboxIdempotencyChecker.ShouldProcessAsync()` attempts to insert the message ID into the database. The first time it succeeds (`true`). The second time, the insert fails (UNIQUE constraint violation) and the method returns `false`, telling you to skip the duplicate safely.

### What happens if my database goes down?

The `OutboxDispatcherBackgroundService` is a BackgroundService that continuously polls the outbox table using `SELECT ... FOR UPDATE SKIP LOCKED`. If the database is unavailable, the dispatcher pauses without crashing your application, resuming with adaptive backoff when the connection recovers.

### What is Adaptive Polling?

A feature of `OutboxDispatcherOptions`. When the dispatcher finds no pending messages, it dynamically increases the polling interval to avoid saturating the database with empty queries. The moment a new message arrives, it snaps back to maximum speed.

### Does the library support NativeAOT?

Yes, fully. The library uses:
- Explicit `IOutboxMessageTypeResolver` (no reflection at runtime)
- `System.Text.Json` source generators for AOT-compatible serialization
- `readonly record struct` and `ref struct` types to avoid boxing

100% compatible with Trimmed and Ahead-Of-Time (AOT) compilation on .NET 8+.

### Which databases are supported?

| Database | Package | Lock Strategy |
|---|---|---|
| PostgreSQL | `Storage.PostgreSql` | `FOR UPDATE SKIP LOCKED` |
| SQL Server | `Storage.SqlServer` | `WITH (UPDLOCK, READPAST)` |
| MySQL 8.0+ | `Storage.MySql` | `FOR UPDATE SKIP LOCKED` |
| Oracle 12c+ | `Storage.Oracle` | `FOR UPDATE SKIP LOCKED` |
| SQLite | `Storage.Sqlite` | WAL-mode table lock (single instance only) |
| Any EF Core provider | `EntityFrameworkCore` | Via EF Core transactions |

### Can I use this with a NoSQL database like MongoDB?

The core library is designed around relational databases with ACID transactions. However, you can implement your own `IOutboxRepository` for document databases. The `IOutboxTransactionContext.GetContext<T>()` escape hatch (Default Interface Method) was designed specifically for this — see the Marten example in the [API reference](api-reference.md#6-persistence--transaction-context).

### What happens to messages marked as `Dispatched`?

Successfully dispatched messages are **deleted** from the outbox table immediately (Delete-on-Dispatch strategy). This prevents table bloat and keeps index performance optimal. If you need historical auditing, use a CDC (Change Data Capture) system or export to a data warehouse.

---

## Common Problems & Solutions

### 1. Messages stuck in `Pending` state

**Symptom:** `StoreAsync()` succeeds, but messages never reach the broker and remain in `Pending` state.

**Solutions:**
- Verify you registered the background dispatcher: `builder.Services.AddOutboxDispatcher();`
- Check health checks. If the database is unreachable from the BackgroundService, polling will pause temporarily.
- If using `DeliverAt` or `WithDelay`, verify the server timezone. The `deliver_at` column is stored in UTC.

### 2. Duplicate messages (massive duplication)

**Symptom:** The same message is published 5+ times to the same consumer in a short window.

**Solutions:**
- This typically occurs when the broker's acknowledgment timeout is shorter than the actual publish time. If publishing takes 10 seconds but your socket timeout is 2 seconds, the dispatcher cancels, retries, and re-sends — but the broker had already received the original.
- **Implement the Inbox pattern** to make consumers immune to duplicates. See [Level 3: Real-World Use Cases](showcase/level-03-real-use-cases.md) and the cookbook for recipes.

### 3. `PostgresException: 42P01: relation "OutboxMessages" does not exist`

**Symptom:** The application crashes on startup trying to read the outbox table.

**Solutions:**
- You haven't run database migrations or initialization scripts.
- If using EF Core, call `ApplyOutboxEntityConfigurations()` in your `DbContext.OnModelCreating()` and generate a migration:
  ```bash
  dotnet ef migrations add AddOutboxTables
  dotnet ef database update
  ```
- If using raw ADO.NET storage providers, run the SQL schema scripts provided in each provider's documentation.

### 4. `InvalidOperationException: Type not found for alias X`

**Symptom:** The dispatcher throws when trying to deserialize a message.

**Solutions:**
- The message type alias is not registered. Verify the class is decorated with `[OutboxMessage("X")]`.
- If using source generators, ensure the type is included in your `JsonSerializerContext` partial class.
- If using `InMemoryMessageTypeResolver` (reflection-based), verify the assembly containing the type is loaded.

### 5. High CPU usage from the dispatcher

**Symptom:** The dispatcher process consumes excessive CPU even when the outbox table is empty.

**Solutions:**
- Enable adaptive polling: `options.UseAdaptivePolling = true` (default). This automatically backs off when no messages are found.
- If using a custom `IRetryPolicy`, ensure it doesn't produce zero-duration delays.
- Check `MaxDegreeOfParallelism` — setting this too high on a low-volume system wastes CPU on idle workers.
