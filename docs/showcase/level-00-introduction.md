<!-- Copyright © Erickson Lopez. MIT License. -->

# Level 0: Introduction to the Outbox Pattern

## What is the Transactional Outbox Pattern?

The **Transactional Outbox** is an architectural pattern for distributed systems (microservices, event-driven architectures) that solves the problem of **guaranteeing event publication atomically** with database state changes.

Instead of your application attempting to update the database AND publish an event to a Message Broker (e.g., RabbitMQ, Kafka) as two independent operations, the application **stores the event in a special table (the "Outbox") within the same database transaction**. A background process (the Dispatcher) continuously reads this table and publishes events asynchronously to the Broker.

## The Problem: Dual-Write

Consider an order service that must:
1. Save an order to its local database (PostgreSQL).
2. Publish an `OrderCreatedEvent` to RabbitMQ so the billing service can process it.

```csharp
// ❌ ANTI-PATTERN: Dual-Write Problem
public async Task CreateOrder(Order order, CancellationToken ct)
{
    // 1. Save to local database
    await _dbContext.Orders.AddAsync(order, ct);
    await _dbContext.SaveChangesAsync(ct);

    // 💥 What if the server crashes here? Or the network drops?
    
    // 2. Publish to RabbitMQ
    await _brokerPublisher.PublishAsync(new OrderCreatedEvent(order.Id), ct);
}
```

**If a failure occurs at 💥:**
- The order is saved in the database.
- The event never reaches RabbitMQ.
- The billing service never learns about the order — a **permanent data inconsistency** in your distributed system.

## The Solution: Transactional Outbox

With `EricksonLopez.Outbox`, the flow becomes atomic:

```csharp
// ✅ CORRECT: Transactional Outbox Pattern
public async Task CreateOrder(Order order, CancellationToken ct)
{
    await using var transaction = await _dbContext.Database.BeginTransactionAsync(ct);

    // 1. Save to local database
    await _dbContext.Orders.AddAsync(order, ct);

    // 2. Store the event in the Outbox table (same transaction)
    var integrationEvent = new OrderCreatedEvent(order.Id);
    await _outbox.StoreAsync(integrationEvent, transactionContext, ct);

    // 3. Commit — both the order AND the event are persisted atomically.
    //    If this fails, BOTH are rolled back. No inconsistency.
    await _dbContext.SaveChangesAsync(ct);
    await transaction.CommitAsync(ct);
}
```

The **Dispatcher** background service will then pick up the event and deliver it to the broker reliably.

## Advantages

| Advantage | Description |
|---|---|
| **Absolute Atomicity** | If the business entity is saved, the event is **guaranteed** to be published. |
| **At-Least-Once Delivery** | Even if the broker or network goes down, the message remains in the Outbox table until successfully delivered. |
| **Resilience** | The main application does not depend on the immediate availability of the broker to continue operating. |
| **Write Performance** | Saving an event to the same local database is much faster and less failure-prone than making a synchronous network call to a broker. |

## Trade-offs

| Trade-off | Mitigation |
|---|---|
| **Publication Latency** | Events are dispatched asynchronously — there's a small delay (milliseconds) between the transaction and the broker delivery. The `AdaptivePoller` minimizes this to near-real-time. |
| **Idempotency Required** | The system guarantees *At-Least-Once* delivery, meaning duplicates are possible during crash recovery. Consumers must be idempotent — `EricksonLopez.Outbox` includes a built-in **Inbox** pattern for this. |
| **Extra Infrastructure** | Requires additional database tables and a running Dispatcher process — but `EricksonLopez.Outbox` manages all of this for you via `AddOutboxDispatcher()`. |

## Comparison with Distributed Transactions (2PC / DTC)

| Criterion | Two-Phase Commit (2PC) | Transactional Outbox |
|---|---|---|
| **Availability** | Low (if the broker is down, the entire TX fails) | **High** (local system continues operating) |
| **Latency** | Very high (heavyweight coordination) | **Very low** (local database write) |
| **Cloud Support** | Sparse (most cloud brokers don't support it) | **Universal** (only requires a standard ACID database) |
| **Scalability** | Anti-pattern in modern architectures | **Industry standard** for microservices |

---

**Next:** In [Level 1](level-01-getting-started.md), you will install the library and publish your first guaranteed event.
