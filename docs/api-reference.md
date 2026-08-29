<!-- Copyright © Erickson Lopez. MIT License. -->

# Public API Reference Guide (Microsoft Learn Format)

This document provides the official technical reference for all public APIs in the `EricksonLopez.Outbox` and `EricksonLopez.Inbox` ecosystem.

---

## Table of Contents

1. [Core Producer API (`EricksonLopez.Outbox.Abstractions` & `EricksonLopez.Outbox`)](#1-core-producer-api)
2. [Fluent Message Builder API (`OutboxMessageBuilder<T>`)](#2-fluent-message-builder-api)
3. [Configuration & Dependency Injection](#3-configuration--dependency-injection)
4. [Dispatcher & Background Processing](#4-dispatcher--background-processing)
5. [Persistence & Transaction Contexts](#5-persistence--transaction-contexts)
6. [Resilience & Retry Policies](#6-resilience--retry-policies)
7. [Serialization & Type Resolvers](#7-serialization--type-resolvers)
8. [Inbox & Consumer Idempotency](#8-inbox--consumer-idempotency)
9. [Storage Providers Reference](#9-storage-providers-reference)
10. [Broker Publishers Reference](#10-broker-publishers-reference)
11. [Testing & In-Memory Verification API](#11-testing--in-memory-verification-api)

---

## 1. Core Producer API

### `IOutbox` Interface

**Namespace:** `EricksonLopez.Outbox`  
**Assembly:** `EricksonLopez.Outbox.Abstractions.dll`

The primary contract for storing messages atomically within a database transaction.

#### Methods

##### `StoreAsync<TMessage>(TMessage, IOutboxTransactionContext, CancellationToken)`

Stores a single message in the outbox table within the specified transaction.

```csharp
ValueTask StoreAsync<TMessage>(
    TMessage message,
    IOutboxTransactionContext transaction,
    CancellationToken cancellationToken = default) where TMessage : notnull;
```

- **Parameters:**
  - `message` (`TMessage`): The domain or integration event instance to store. Cannot be null.
  - `transaction` (`IOutboxTransactionContext`): The ambient database transaction context (e.g. `DbTransactionContext`).
  - `cancellationToken` (`CancellationToken`): Token to observe while waiting for the task to complete.
- **Returns:** `ValueTask` representing the asynchronous store operation.
- **Exceptions:**
  - `ArgumentNullException`: Thrown if `message` or `transaction` is null.
  - `OutboxException`: Thrown if the message type is not registered and `ThrowOnUnregisteredType=true`.
  - `PayloadTooLargeException`: Thrown if the serialized payload exceeds `MaxPayloadSizeInBytes`.
- **Remarks:** This method is atomic. If the surrounding database transaction is committed, the message is guaranteed to be persisted. If rolled back, the message is discarded.
- **Example:**
```csharp
await using var conn = await dataSource.OpenConnectionAsync(ct);
await using var tx = await conn.BeginTransactionAsync(ct);

var @event = new OrderCreatedEvent(order.Id, order.CustomerId, order.Total, DateTimeOffset.UtcNow);
await outbox.StoreAsync(@event, tx.ToOutboxContext(), ct);

await tx.CommitAsync(ct);
```

---

##### `StoreAsync<TMessage>(ReadOnlyMemory<TMessage>, IOutboxTransactionContext, CancellationToken)`

Stores a batch of messages in the outbox table using a zero-allocation `ReadOnlyMemory<TMessage>` slice.

```csharp
ValueTask StoreAsync<TMessage>(
    ReadOnlyMemory<TMessage> messages,
    IOutboxTransactionContext transaction,
    CancellationToken cancellationToken = default) where TMessage : notnull;
```

- **Parameters:**
  - `messages` (`ReadOnlyMemory<TMessage>`): Contiguous memory slice of messages to insert in a single SQL batch.
  - `transaction` (`IOutboxTransactionContext`): The ambient transaction context.
  - `cancellationToken` (`CancellationToken`): Cancellation token.
- **Performance:** Zero intermediate heap allocation; ideal for high-throughput batching loops with pre-rented buffers.

---

##### `StoreAsync<TMessage>(TMessage, IOutboxTransactionContext, OutboxMessageMetadata, DateTimeOffset?, CancellationToken)`

Stores a message with explicit pre-built metadata and optional delayed delivery timestamp.

```csharp
ValueTask StoreAsync<TMessage>(
    TMessage message,
    IOutboxTransactionContext transaction,
    OutboxMessageMetadata metadata,
    DateTimeOffset? deliverAt,
    CancellationToken cancellationToken = default) where TMessage : notnull;
```

- **Parameters:**
  - `metadata` (`OutboxMessageMetadata`): Struct containing CorrelationId, CausationId, and custom headers.
  - `deliverAt` (`DateTimeOffset?`): Timestamp when the message becomes eligible for polling. If null, dispatched immediately.

---

##### `Publish<TMessage>(TMessage)`

Initializes the fluent builder for fine-grained message configuration before storing.

```csharp
OutboxMessageBuilder<TMessage> Publish<TMessage>(TMessage message) where TMessage : notnull;
```

- **Returns:** `OutboxMessageBuilder<TMessage>` — a `sealed class` (heap-allocated, `IDisposable`). Dispose is handled automatically by `await StoreAsync()`. Do **not** manually call `Dispose()` unless aborting the fluent chain early.

---

## 2. Fluent Message Builder API

### `OutboxMessageBuilder<TMessage>`

**Namespace:** `EricksonLopez.Outbox`  
**Assembly:** `EricksonLopez.Outbox.dll`  
**Type:** `sealed class` implementing `IDisposable`

> [!NOTE]
> `OutboxMessageBuilder<TMessage>` is a **heap-allocated class**, not a `ref struct`. It rents an internal
> `MetadataEntry[]` from `ArrayPool<MetadataEntry>.Shared` when headers are added, and returns it on
> disposal. The builder is automatically disposed by `StoreAsync()`. If you abandon the fluent chain
> without calling `StoreAsync()`, dispose the builder manually to avoid a pool buffer leak. See [ADR-037](adr/037-outboxmessagebuilder-sealed-class-rationale.md).

#### Methods

| Method Signature | Description |
| :--- | :--- |
| `WithTransaction(IOutboxTransactionContext transaction)` | Binds the target database transaction context. |
| `WithCorrelationId(string correlationId)` | Sets the distributed tracing Correlation ID header. |
| `WithCausationId(string causationId)` | Sets the Causation ID header indicating the command origin. |
| `WithHeader(string key, string value)` | Adds a custom metadata key-value header entry. |
| `WithTenantId(string tenantId)` | Adds the reserved `x-tenant-id` header (shortcut for `WithHeader("x-tenant-id", tenantId)`). |
| `WithDelay(TimeSpan delay)` | Schedules delivery after a specified time delay from now. Sets the `deliver_at` column. |
| `WithDeliverAt(DateTimeOffset deliverAt)` | Schedules delivery at an exact absolute UTC timestamp. Sets the `deliver_at` column. |
| `StoreAsync(CancellationToken ct = default)` | Persists the configured message into the database and disposes the builder. |

#### Example
```csharp
await outbox.Publish(@event)
    .WithCorrelationId(correlationId)
    .WithCausationId(commandId)
    .WithTenantId(tenantId)           // idiomatic: sets x-tenant-id header
    .WithHeader("X-Source-System", "showcase")
    .WithDelay(TimeSpan.FromMinutes(10))
    .WithTransaction(tx.ToOutboxContext())
    .StoreAsync(cancellationToken);
```

---

## 3. Configuration & Dependency Injection

### `OutboxServiceCollectionExtensions`

**Namespace:** `EricksonLopez.Outbox`

```csharp
public static class OutboxServiceCollectionExtensions
{
    // Registers core Outbox services (IOutbox, Serializer, TypeResolver)
    public static IServiceCollection AddOutbox(
        this IServiceCollection services, 
        Action<OutboxOptions> configure);

    // Registers the background Dispatcher daemon
    public static IServiceCollection AddOutboxDispatcher(
        this IServiceCollection services, 
        Action<OutboxDispatcherOptions> configure);

    // Registers the Inbox deduplication retention daemon
    public static IServiceCollection AddOutboxInbox(
        this IServiceCollection services, 
        Action<OutboxInboxOptions> configure);
}
```

> [!TIP]
> To purge dispatched messages in soft-delete mode (`DeleteOnDispatch = false`), call
> `IOutboxRepository.PurgeDispatchedMessagesAsync(cutoff, batchSize, ct)` directly from your own
> `IHostedService` or scheduled job (e.g., Hangfire, NCronJob, Quartz.NET).

### `OutboxOptions` Routing API

| Method | Returns | Description |
|---|---|---|
| `Route(string alias)` | `BrokerRouteBuilder` | Configures a single alias to a specific publisher. |
| `RouteGroup(params string[] aliases)` | `BrokerRouteGroupBuilder` | Configures multiple aliases to the same publisher (params array). |
| `RouteGroup(IEnumerable<string> aliases)` | `BrokerRouteGroupBuilder` | Configures multiple aliases from any enumerable. |

### Options Classes

#### `OutboxDispatcherOptions`
- `BatchSize` (`int`, default: 100): Maximum messages fetched per poll.
- `MaxDegreeOfParallelism` (`int`, default: `min(ProcessorCount, 8)`): Concurrent pipeline dispatch tasks.
- `PollingInterval` (`TimeSpan`, default: 500ms): Poller sleep duration when queue is empty.
- `UseAdaptivePolling` (`bool`, default: true): Dynamically switches between 0ms (under load) and PollingInterval (idle).
- `ChannelCapacity` (`int`, default: 1000): Capacity of internal bounded channel for worker backpressure.
- `MaxBatchesPerSecond` (`int`, default: 0): Rate limit for backlog drain (0 = unlimited).
- `MaxRetryCount` (`int`, default: 10): Max retries before dead-lettering.
- `ReclaimTimeout` (`TimeSpan`, default: 5 min): InFlight lock expiration for crash recovery.
- `ReclaimInterval` (`TimeSpan`, default: 1 min): Frequency of the stale message recovery job.
- `HasOnlySingletonMiddlewares` (`bool`, default: false): Caches pipeline delegate per batch when all middlewares are Singleton.

---

## 4. Dispatcher & Resilience

### `DispatchResult`

**Namespace:** `EricksonLopez.Outbox`  
**Type:** `readonly record struct`

Returned by `IBrokerPublisher.PublishRawAsync()` to signal publication outcome.

```csharp
public readonly record struct DispatchResult
{
    public bool Success { get; }
    public bool ShouldRetry { get; }
    public bool IncrementRetryCount { get; }
    public Exception? Exception { get; }
    public string? ErrorMessage { get; }

    public static DispatchResult Ok();
    public static DispatchResult FailAndRetry(Exception ex, bool incrementRetryCount = true);
    public static DispatchResult FailFatal(Exception ex);
    public static DispatchResult FailFatal(string errorMessage);
}
```

### `IBrokerPublisher`

**Namespace:** `EricksonLopez.Outbox`

The minimal contract required by the Outbox dispatcher. All broker adapter packages implement this interface.

```csharp
public interface IBrokerPublisher
{
    // Called exclusively by the Outbox dispatcher. Works on pre-serialized payloads
    // for NativeAOT compatibility — no runtime generics required.
    ValueTask<DispatchResult> PublishRawAsync(
        OutboxMessage message,
        OutboxMessageMetadata metadata,
        DispatchContext context);

    // Default Interface Method — returns the OpenTelemetry messaging.system tag for this broker.
    // Override in your implementation (e.g., return "rabbitmq", "kafka", "azure_service_bus").
    // Default fallback: "outbox".
    string BrokerSystemName => Diagnostics.OutboxActivitySource.OutboxSystemName;
}
```

> [!IMPORTANT]
> **Do NOT throw exceptions** from `PublishRawAsync`. Catch all broker exceptions and map them to
> `DispatchResult.FailAndRetry(ex)` (transient) or `DispatchResult.FailFatal(ex)` (unrecoverable).
> Uncaught exceptions are treated as fatal by the dispatcher and will dead-letter the message.

### `ITypedBrokerPublisher`

**Namespace:** `EricksonLopez.Outbox`

Extends `IBrokerPublisher` with strongly-typed publishing overloads. Implement this interface (**in addition to** `IBrokerPublisher`) if your broker adapter supports typed envelope publishing.

```csharp
public interface ITypedBrokerPublisher : IBrokerPublisher
{
    // Publishes a single strongly-typed message envelope.
    ValueTask<DispatchResult> PublishAsync<T>(
        MessageEnvelope<T> message,
        DispatchContext context) where T : notnull;

    // Publishes a batch of strongly-typed message envelopes.
    ValueTask<IReadOnlyList<DispatchResult>> PublishBatchAsync<T>(
        IReadOnlyList<MessageEnvelope<T>> messages,
        DispatchContext context) where T : notnull;
}
```

---

## 5. Inbox & Idempotency API

### `IInboxIdempotencyChecker`

**Namespace:** `EricksonLopez.Outbox.Idempotency`

```csharp
public interface IInboxIdempotencyChecker
{
    // Returns true if the message should be processed (idempotency record inserted successfully).
    // Returns false if the message was already processed (duplicate detected).
    Task<bool> ShouldProcessAsync(
        string messageId,
        string consumerId,
        IOutboxTransactionContext transaction,
        CancellationToken cancellationToken = default);

    // Returns true if the message has already been processed and should be SKIPPED.
    // Returns false if the message is new and should be processed.
    // consumerId defaults to OutboxConstants.DispatcherConsumerId for internal dispatcher use.
    // Always provide a unique, stable consumer-specific ID from user-facing consumers.
    Task<bool> ShouldSkipAsync(
        Guid messageId,
        IOutboxTransactionContext transaction,
        string consumerId = OutboxConstants.DispatcherConsumerId,
        CancellationToken cancellationToken = default);
}
```

> [!WARNING]
> Both methods return `Task<bool>`, **not** `ValueTask<bool>`. The return type is intentionally `Task`
> because the underlying database operation involves I/O that cannot be completed synchronously.
> Note also that `ShouldSkipAsync` takes a `Guid messageId`, not a `string`.

---

## 6. Testing & In-Memory Verification API

### `InMemoryOutboxStore`

**Namespace:** `EricksonLopez.Outbox.Testing`

Provides a complete, in-memory implementation of `IOutbox` for unit and integration testing without database dependencies.

```csharp
public sealed class InMemoryOutboxStore : IOutbox
{
    // Retrieve all messages stored of type TMessage.
    public IReadOnlyList<TMessage> GetPublishedMessages<TMessage>() where TMessage : notnull;

    // Reset all stored messages (use between tests).
    public void Reset();

    // Fluent builder — same API as production IOutbox.
    public OutboxMessageBuilder<TMessage> Publish<TMessage>(TMessage message) where TMessage : notnull;
}
```

#### Assertion Extensions (`TestingOutboxExtensions`):
- `store.ShouldHavePublished<T>()`
- `store.ShouldHavePublished<T>(predicate)`
- `store.ShouldHavePublishedOnce<T>()`
- `store.ShouldHavePublishedTimes<T>(n)`
- `store.ShouldNotHavePublished<T>()`

> [!NOTE]
> `InMemoryOutboxStore` does **not** expose a `PublishedMessages` property returning `IReadOnlyList<object>`.
> Use the generic `GetPublishedMessages<TMessage>()` method to retrieve typed messages.

---

## 7. Persistence Administration API (`IOutboxRepository`)

**Namespace:** `EricksonLopez.Outbox.Persistence`

Key administrative methods available on `IOutboxRepository`:

| Method | DIM? | Description |
|---|---|---|
| `GetPendingCountAsync(ct)` | No | Approximate count of messages in **Pending (0) or Failed (3)** state. Used for metrics and monitoring. Uses catalog estimate for large tables in PostgreSQL. |
| `GetMessageAsync(Guid id, ct)` | Yes | Look up a single message by ID (any state). Throws `NotSupportedException` if not overridden. |
| `GetMessageAsync(Guid id, DateTimeOffset createdAtHint, ct)` | Yes | Same as above with PostgreSQL partition pruning hint. |
| `PurgeDispatchedMessagesAsync(DateTimeOffset cutoff, int batchSize, ct)` | No | Deletes dispatched messages older than cutoff. **Only effective when `DeleteOnDispatch = false`.** |

> [!NOTE]
> **DIM** = Default Interface Method. The method has a default implementation on the interface that throws `NotSupportedException`. Concrete storage implementations may override it.

---

## 8. `Publisher` Struct

**Namespace:** `EricksonLopez.Outbox`  
**Type:** `readonly record struct`

Lightweight identity for a logical publisher node in multi-instance or multi-publisher topologies.

```csharp
public readonly record struct Publisher
{
    public string Id { get; }           // Unique ID (Guid, format "N" — no dashes)
    public string Name { get; }         // Human-readable node name
    public DateTimeOffset RegisteredAt { get; }  // Timestamp when Create() was called

    public static Publisher Create(string name); // Auto-generates a unique Id + RegisteredAt = UtcNow
    public static Publisher None { get; }        // Null-object: Id = "0...0", Name = "none"
}
```

---

## 9. Multi-Tenancy Interfaces

**Namespace:** `EricksonLopez.Outbox.MultiTenancy`

Both interfaces are opt-in extension points resolved via DI. Neither is registered automatically.

### `ITenantBrokerRouter`

```csharp
public interface ITenantBrokerRouter
{
    // Resolves the final topic/queue destination for a given tenant.
    // Returns baseDestination if tenantId is null (no tenant context).
    string ResolveDestination(string? tenantId, string baseDestination, string messageType);
}
```

### `ITenantConnectionResolver`

```csharp
public interface ITenantConnectionResolver
{
    // Resolves the DB connection string for the specified tenant.
    ValueTask<string> ResolveConnectionStringAsync(string tenantId, CancellationToken ct = default);
}
```

The `x-tenant-id` header (set via `OutboxMessageBuilder.WithTenantId()`) is the contract between the producer and `ITenantBrokerRouter`. The header value is passed as the `tenantId` parameter to `ResolveDestination()`.

---

## 10. Delayed Delivery (`deliver_at`)

The Outbox supports deferred message delivery via the `deliver_at` database column. This is **not** a scheduler — it is a visibility timeout mechanism. The dispatcher polls only messages where `deliver_at IS NULL OR deliver_at <= NOW()`.

**API surface for setting `deliver_at`:**

```csharp
// Via the fluent builder:
await outbox.Publish(@event)
    .WithDelay(TimeSpan.FromMinutes(30))          // deliver_at = UtcNow + 30 min
    .WithTransaction(tx.ToOutboxContext())
    .StoreAsync(ct);

await outbox.Publish(@event)
    .WithDeliverAt(DateTimeOffset.UtcNow.AddHours(2))  // explicit absolute timestamp
    .WithTransaction(tx.ToOutboxContext())
    .StoreAsync(ct);

// Via the explicit overload:
await outbox.StoreAsync(
    @event,
    tx.ToOutboxContext(),
    metadata,
    deliverAt: DateTimeOffset.UtcNow.AddMinutes(15),
    ct);
```

> [!NOTE]
> See [ADR-025](adr/025-no-scheduler.md) — the Outbox is not a scheduler. For recurring jobs,
> use Hangfire, Quartz.NET, or NCronJob to enqueue outbox messages at the appropriate time.
