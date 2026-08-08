# Cookbook & Best Practices

Practical recipes for real-world integration patterns with `EricksonLopez.Outbox`, plus production best practices. All recipes use exclusively the verified public API.

---

## Recipes

### Recipe 1: Transaction with Raw ADO.NET (PostgreSQL)

**Problem:** You need to persist an event in the outbox within the same transaction as your domain operations using `Npgsql`.

```csharp
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Persistence;
using Npgsql;

public async Task ProcessOrder(IOutbox outbox, NpgsqlDataSource dataSource, CancellationToken ct)
{
    await using var conn = await dataSource.OpenConnectionAsync(ct);
    await using var tx = await conn.BeginTransactionAsync(ct);

    // 1. Execute domain query
    await using var cmd = new NpgsqlCommand("INSERT INTO Orders (Id) VALUES (@id)", conn, tx);
    cmd.Parameters.AddWithValue("id", Guid.NewGuid());
    await cmd.ExecuteNonQueryAsync(ct);

    // 2. Store event in outbox (same transaction)
    var @event = new OrderCreatedEvent(Guid.NewGuid(), 99.99m);
    var dbTx = new DbTransactionContext(tx);
    await outbox.StoreAsync(@event, dbTx, ct);

    // 3. Unified commit — both succeed or both rollback
    await tx.CommitAsync(ct);
}
```

### Recipe 2: Transaction with Entity Framework Core

**Problem:** You have a `DbContext` and need to ensure atomicity between your entities and the outbox message.

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Persistence;

public async Task SaveWithEfCore(AppDbContext dbContext, IOutbox outbox, CancellationToken ct)
{
    await using var tx = await dbContext.Database.BeginTransactionAsync(ct);

    // 1. Modify entities
    var order = new Order { CustomerId = "123" };
    dbContext.Orders.Add(order);
    await dbContext.SaveChangesAsync(ct);

    // 2. Store in outbox (extract the underlying DbTransaction)
    var @event = new OrderCreatedEvent(order.Id);
    var dbTx = new DbTransactionContext(tx.GetDbTransaction());
    await outbox.StoreAsync(@event, dbTx, ct);

    // 3. Commit the transaction
    await tx.CommitAsync(ct);
}
```

### Recipe 3: Fluent API with Metadata and Delayed Delivery

**Problem:** You need to add `CorrelationId`, `CausationId`, custom headers, and schedule delayed delivery.

```csharp
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Persistence;

public async Task PublishWithMetadata(IOutbox outbox, DbTransaction tx, CancellationToken ct)
{
    var @event = new UserRegisteredEvent("user@domain.com");

    await outbox.Publish(@event)
        .WithCorrelationId(Guid.NewGuid().ToString())
        .WithCausationId("cmd-register-user-42")
        .WithHeader("X-Tenant-Id", "tenant-alpha")
        .WithDelay(TimeSpan.FromMinutes(5))  // Delay delivery by 5 minutes
        .WithTransaction(new DbTransactionContext(tx))
        .StoreAsync(ct);
}
```

### Recipe 4: Batch Processing

**Problem:** You have hundreds of events to publish and individual `StoreAsync` calls are too slow.

```csharp
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Persistence;

public async Task PublishBatch(
    IOutbox outbox, DbTransaction tx, IEnumerable<MyEvent> events, CancellationToken ct)
{
    var dbTx = new DbTransactionContext(tx);
    // The outbox converts the enumerable and inserts in a single batch round-trip
    await outbox.StoreAsync(events, dbTx, ct);
}
```

### Recipe 5: Idempotent Consumption (Inbox Pattern)

**Problem:** You're receiving messages from RabbitMQ or Kafka, but the broker guarantees at-least-once delivery. You need to prevent processing the same message twice.

**How it works:**
1. When a message arrives, start a local transaction.
2. Attempt to insert the `MessageId` + `ConsumerId` into the idempotency table:
   ```sql
   INSERT INTO outbox.idempotency (message_id, consumer_id, processed_at)
   VALUES (@MessageId, @ConsumerId, NOW())
   ON CONFLICT DO NOTHING;
   ```
3. If zero rows affected → the message was already processed. Skip it.
4. If one row inserted → execute business logic within the **same** transaction.
5. Commit atomically — both the business state and the idempotency record are saved together.

```csharp
using EricksonLopez.Outbox.Idempotency;
using EricksonLopez.Outbox.Persistence;

public async Task ConsumeMessage(
    string messageId,
    OrderCreatedEvent payload,
    DbTransaction tx,
    IInboxIdempotencyChecker checker,
    CancellationToken ct)
{
    var dbTx = new DbTransactionContext(tx);

    // Attempt to claim exclusive processing rights
    bool shouldProcess = await checker.ShouldProcessAsync(
        messageId,
        consumerId: "BillingService",
        dbTx,
        ct);

    if (!shouldProcess)
    {
        // Duplicate — ACK the broker and skip
        return;
    }

    // Execute business logic (safe — only runs once per unique messageId)
    await ProcessPayment(payload);
}
```

> [!NOTE]
> If the server crashes during business logic execution, the transaction rolls back.
> The idempotency record disappears, the business state doesn't change, and the broker
> will redeliver the message, where it will succeed on the next attempt.

### Recipe 6: Domain Events in DDD Aggregates

**Problem:** You follow DDD and want to collect domain events inside your aggregate root, then flush them to the outbox during persistence.

```csharp
public abstract class AggregateRoot
{
    private readonly List<object> _domainEvents = [];
    public IReadOnlyList<object> DomainEvents => _domainEvents;
    protected void AddDomainEvent(object evt) => _domainEvents.Add(evt);
    public void ClearDomainEvents() => _domainEvents.Clear();
}

// In your Unit of Work / SaveChanges override:
public async Task CommitAsync(CancellationToken ct)
{
    await using var tx = await _db.Database.BeginTransactionAsync(ct);
    var txContext = new DbTransactionContext(tx.GetDbTransaction());

    var aggregates = _db.ChangeTracker.Entries<AggregateRoot>()
        .Where(e => e.Entity.DomainEvents.Count > 0)
        .Select(e => e.Entity);

    foreach (var aggregate in aggregates)
    {
        foreach (var evt in aggregate.DomainEvents)
            await _outbox.StoreAsync(evt, txContext, ct);
        aggregate.ClearDomainEvents();
    }

    await _db.SaveChangesAsync(ct);
    await tx.CommitAsync(ct);
}
```

---

## Production Best Practices

### 1. Database Configuration

- **Use native storage providers** (`PostgreSqlOutboxRepository`, `SqlServerOutboxRepository`) for horizontal scaling. They implement `SKIP LOCKED` / `READPAST` for safe concurrent polling.
- **Do not delete indexes** created by the library on `state`, `created_at`, and `deliver_at` columns.
- **Tune autovacuum** for the outbox table (PostgreSQL):
  ```sql
  ALTER TABLE outbox.messages SET (
      autovacuum_vacuum_scale_factor = 0.01,
      autovacuum_analyze_scale_factor = 0.01,
      autovacuum_vacuum_cost_delay = 2
  );
  ```

### 2. Message Design

- **Keep payloads small.** The outbox is not for binary files. Use the **Claim Check Pattern** — upload to blob storage (S3/Azure Blob) and send the URL in the event.
- **Always use aliases.** Use `[OutboxMessage("domain.entity.event.v1")]`. If you rename your namespace, the alias protects deserialization of old messages.
- **Use source generators** in production: `options.UseGeneratedTypes()`. Zero-allocation, AOT-friendly.

### 3. Dispatcher Tuning

- **`BatchSize`:** Start with 100. Reduce if the broker can't keep up; increase to 500–1000 for high throughput.
- **`UseAdaptivePolling`:** Always enable. Prevents thousands of empty SELECTs during off-peak hours.
- **`MaxDegreeOfParallelism`:** Use 1 for strict ordering. Use 4–8 for high-throughput with relaxed ordering.

### 4. Data Retention

- Configure `options.RetentionPeriod` to 7 or 15 days for inbox cleanup.
- Do not keep dispatched events forever in the transactional database — this degrades index performance.
- For historical auditing, use CDC (Change Data Capture) or a data warehouse.

### 5. Consumer Design (Idempotency)

- Design consumers assuming they **will** receive the same message twice.
- If your business logic is not naturally idempotent (e.g., `UPDATE balance = balance - 10`), **you must** use the `IInboxIdempotencyChecker` (Inbox Pattern).

### 6. Strict Ordering

The outbox guarantees partial ordering (by `created_at`). For strict per-entity ordering:
1. Group logically by `AggregateId`
2. Set `MaxDegreeOfParallelism = 1`
3. Pass the `AggregateId` as a Kafka partition key via `OutboxMessageBuilder.WithHeader()`

### 7. Observability

```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource("EricksonLopez.Outbox"));
```

The framework automatically propagates `traceparent` inside message headers for distributed trace correlation.

---

## Running the Sample Application

The repository includes a functional example under `samples/Sample.OrderService`:

```bash
# 1. Start infrastructure (PostgreSQL + RabbitMQ)
cd samples/Sample.OrderService
docker-compose up -d

# 2. Run the application
dotnet run
```

The sample demonstrates the complete flow: `POST /orders` → save order + store outbox event → dispatcher publishes to RabbitMQ → consumer processes with inbox idempotency.
