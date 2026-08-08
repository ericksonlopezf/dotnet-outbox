# API Reference

This document provides a concise reference of the public API surface for `EricksonLopez.Outbox`.
All signatures are extracted directly from the source code. For full XML doc comments, refer to
the IntelliSense documentation in your IDE or the source files linked below.

> [!TIP]
> For auto-generated, always-up-to-date API documentation, consider integrating
> [DocFX](https://dotnet.github.io/docfx/) into the CI pipeline. The source code
> already contains comprehensive XML doc comments on all public types.

---

## Table of Contents

1. [Core — Producer API](#1-core--producer-api)
2. [Core — Dispatcher & Resilience](#2-core--dispatcher--resilience)
3. [Core — Serialization](#3-core--serialization)
4. [Core — Idempotency (Inbox)](#4-core--idempotency-inbox)
5. [Persistence — Repositories](#5-persistence--repositories)
6. [Persistence — Transaction Context](#6-persistence--transaction-context)
7. [Persistence — Data Models](#7-persistence--data-models)
8. [Configuration & DI](#8-configuration--di)
9. [Diagnostics](#9-diagnostics)
10. [Roslyn Tooling](#10-roslyn-tooling)

---

## 1. Core — Producer API

### `IOutbox`

**Namespace:** `EricksonLopez.Outbox`
**Source:** [`IOutbox.cs`](../src/EricksonLopez.Outbox/IOutbox.cs)

The primary entry point for storing messages in the outbox.

```csharp
public interface IOutbox
{
    // Store a single message within a transaction
    ValueTask StoreAsync<TMessage>(
        TMessage message,
        IOutboxTransactionContext transaction,
        CancellationToken cancellationToken = default) where TMessage : notnull;

    // Store a batch of messages (zero-copy via ReadOnlyMemory<T>)
    ValueTask StoreAsync<TMessage>(
        ReadOnlyMemory<TMessage> messages,
        IOutboxTransactionContext transaction,
        CancellationToken cancellationToken = default) where TMessage : notnull;

    // Store with explicit metadata and delayed delivery
    ValueTask StoreAsync<TMessage>(
        TMessage message,
        IOutboxTransactionContext transaction,
        MessageMetadata metadata,
        DateTimeOffset? deliverAt,
        CancellationToken cancellationToken = default) where TMessage : notnull;

    // Fluent builder API
    OutboxMessageBuilder<TMessage> Publish<TMessage>(TMessage message) where TMessage : notnull;
}
```

**Extension methods** (`OutboxExtensions`):

```csharp
// Store an IEnumerable<T> batch (materializes to array internally)
ValueTask StoreAsync<TMessage>(
    this IOutbox outbox,
    IEnumerable<TMessage> messages,
    IOutboxTransactionContext transaction,
    CancellationToken cancellationToken = default) where TMessage : notnull;
```

### `OutboxMessageBuilder<TMessage>`

**Namespace:** `EricksonLopez.Outbox`
**Type:** `ref struct` (stack-only, zero allocation)

Fluent builder returned by `IOutbox.Publish()`.

```csharp
public ref struct OutboxMessageBuilder<TMessage> where TMessage : notnull
{
    OutboxMessageBuilder<TMessage> WithTransaction(IOutboxTransactionContext transaction);
    OutboxMessageBuilder<TMessage> WithDelay(TimeSpan delay);
    OutboxMessageBuilder<TMessage> DeliverAt(DateTimeOffset deliverAt);
    OutboxMessageBuilder<TMessage> WithHeader(string key, string value);
    OutboxMessageBuilder<TMessage> WithCorrelationId(string correlationId);
    OutboxMessageBuilder<TMessage> WithCausationId(string causationId);
    ValueTask StoreAsync(CancellationToken cancellationToken = default);
}
```

---

## 2. Core — Dispatcher & Resilience

### `IBrokerPublisher`

**Namespace:** `EricksonLopez.Outbox`
**Source:** [`IBrokerPublisher.cs`](../src/EricksonLopez.Outbox/Core/IBrokerPublisher.cs)

Implement this to integrate with any message broker.

```csharp
public interface IBrokerPublisher
{
    ValueTask<DispatchResult> PublishRawAsync(
        OutboxMessage message,
        MessageMetadata metadata,
        DispatchContext context);
}

// Optional: strongly-typed publishing
public interface ITypedBrokerPublisher : IBrokerPublisher
{
    ValueTask<DispatchResult> PublishAsync<T>(
        MessageEnvelope<T> message, DispatchContext context) where T : notnull;

    ValueTask<IReadOnlyList<DispatchResult>> PublishBatchAsync<T>(
        IReadOnlyList<MessageEnvelope<T>> messages, DispatchContext context) where T : notnull;
}
```

**Built-in implementations:**

| Class | Package | Broker |
|---|---|---|
| `RabbitMQBrokerPublisher` | `Brokers.RabbitMQ` | RabbitMQ |
| `KafkaBrokerPublisher` | `Brokers.Kafka` | Apache Kafka |
| `AzureServiceBusBrokerPublisher` | `Brokers.AzureServiceBus` | Azure Service Bus |
| `AwsSqsBrokerPublisher` | `Brokers.AwsSqs` | AWS SQS |
| `GooglePubSubBrokerPublisher` | `Brokers.GooglePubSub` | Google Cloud Pub/Sub |
| `NatsBrokerPublisher` | `Brokers.Nats` | NATS |
| `RedisStreamsBrokerPublisher` | `Brokers.RedisStreams` | Redis Streams |
| `MassTransitBrokerPublisher` | `MassTransit` | MassTransit (any transport) |

### `DispatchResult`

**Namespace:** `EricksonLopez.Outbox`

```csharp
public readonly record struct DispatchResult
{
    bool IsSuccess { get; }
    Exception? Error { get; }
    static DispatchResult Success();
    static DispatchResult Failure(Exception error);
}
```

### `IRetryPolicy`

**Namespace:** `EricksonLopez.Outbox.Retry`

```csharp
public interface IRetryPolicy
{
    TimeSpan GetDelay(int retryAttempt);
}
```

**Built-in policies:** `ExponentialBackoffPolicy`, `FixedDelayRetryPolicy`, `JitterRetryPolicy`

### `IOutboxMiddleware`

**Namespace:** `EricksonLopez.Outbox.Pipeline`

```csharp
public interface IOutboxMiddleware
{
    ValueTask<DispatchResult> InvokeAsync(
        DispatchContext context,
        Func<DispatchContext, ValueTask<DispatchResult>> next,
        CancellationToken cancellationToken = default);
}
```

---

## 3. Core — Serialization

### `IOutboxSerializer`

**Namespace:** `EricksonLopez.Outbox.Serialization`
**Source:** [`IOutboxSerializer.cs`](../src/EricksonLopez.Outbox/Serialization/IOutboxSerializer.cs)

```csharp
public interface IOutboxSerializer
{
    ReadOnlyMemory<byte> Serialize<TMessage>(TMessage message);

    // Zero-allocation overload — writes directly to IBufferWriter
    void Serialize<TMessage>(TMessage message, IBufferWriter<byte> buffer);

    TMessage Deserialize<TMessage>(ReadOnlySpan<byte> data);
}
```

**Built-in:** `NativeAotJsonSerializer` (uses `System.Text.Json` with `JsonSerializerContext`)

### `IOutboxMessageTypeResolver`

**Namespace:** `EricksonLopez.Outbox.Serialization`

```csharp
public interface IOutboxMessageTypeResolver
{
    Type? Resolve(string alias);
    string? GetAlias(Type type);
}
```

**Built-in:** `InMemoryMessageTypeResolver`, source-generated resolver via `OutboxTypeMappingGenerator`

---

## 4. Core — Idempotency (Inbox)

### `IInboxIdempotencyChecker`

**Namespace:** `EricksonLopez.Outbox.Idempotency`
**Source:** [`IInboxIdempotencyChecker.cs`](../src/EricksonLopez.Outbox/Idempotency/IInboxIdempotencyChecker.cs)

```csharp
public interface IInboxIdempotencyChecker
{
    Task<bool> ShouldProcessAsync(
        string messageId,
        string consumerId,
        IOutboxTransactionContext transaction,
        CancellationToken cancellationToken = default);

    Task<bool> ShouldSkipAsync(
        Guid messageId,
        IOutboxTransactionContext transaction,
        CancellationToken cancellationToken = default);
}
```

### `IIdempotencyRepository`

**Namespace:** `EricksonLopez.Outbox.Persistence`

```csharp
public interface IIdempotencyRepository
{
    ValueTask<bool> TryInsertAsync(
        IdempotencyRecord record,
        IOutboxTransactionContext? transaction = default,
        CancellationToken cancellationToken = default);

    ValueTask PurgeExpiredRecordsAsync(
        DateTimeOffset olderThan,
        CancellationToken cancellationToken = default);
}
```

---

## 5. Persistence — Repositories

### `IOutboxRepository`

**Namespace:** `EricksonLopez.Outbox.Persistence`
**Source:** [`IOutboxRepository.cs`](../src/EricksonLopez.Outbox/Persistence/IOutboxRepository.cs)

```csharp
public interface IOutboxRepository
{
    ValueTask InsertAsync(
        OutboxMessage record,
        IOutboxTransactionContext transaction,
        CancellationToken cancellationToken = default);

    ValueTask InsertBatchAsync(
        ReadOnlyMemory<OutboxMessage> records,
        IOutboxTransactionContext transaction,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<OutboxMessage>> FetchPendingAsync(
        int batchSize,
        CancellationToken cancellationToken = default);

    ValueTask MarkAsDispatchedAsync(
        IReadOnlyList<OutboxMessage> messages,
        CancellationToken cancellationToken = default);

    ValueTask MarkAsFailedAsync(
        IReadOnlyList<OutboxMessage> messages,
        string error,
        bool isDeadLetter = false,
        CancellationToken cancellationToken = default);

    ValueTask<int> ReclaimStaleMessagesAsync(
        TimeSpan staleTimeout,
        CancellationToken cancellationToken = default);

    ValueTask<long> GetPendingCountAsync(
        CancellationToken cancellationToken = default);
}
```

**Implementations:**

| Class | Package | Database |
|---|---|---|
| `PostgreSqlOutboxRepository` | `Storage.PostgreSql` | PostgreSQL (Npgsql) |
| `SqlServerOutboxRepository` | `Storage.SqlServer` | SQL Server |
| `MySqlOutboxRepository` | `Storage.MySql` | MySQL (MySqlConnector) |
| `OracleOutboxRepository` | `Storage.Oracle` | Oracle |
| `SqliteOutboxRepository` | `Storage.Sqlite` | SQLite |
| `EntityFrameworkCoreOutboxRepository` | `EntityFrameworkCore` | Any EF Core provider |

### `IDeadLetterRepository`

**Namespace:** `EricksonLopez.Outbox.Persistence`

```csharp
public interface IDeadLetterRepository
{
    ValueTask InsertAsync(DeadLetterMessage message, CancellationToken ct = default);
    ValueTask<IReadOnlyList<DeadLetterMessage>> GetAsync(int limit, CancellationToken ct = default);
    ValueTask DeleteAsync(Guid id, CancellationToken ct = default);
    ValueTask PurgeAsync(DateTimeOffset olderThan, CancellationToken ct = default);
}
```

---

## 6. Persistence — Transaction Context

### `IOutboxTransactionContext`

**Namespace:** `EricksonLopez.Outbox.Persistence`
**Source:** [`IOutboxTransactionContext.cs`](../src/EricksonLopez.Outbox/Persistence/IOutboxTransactionContext.cs)

```csharp
public interface IOutboxTransactionContext
{
    object Transaction { get; }
    object? Connection { get; }
    T? GetContext<T>() where T : class;  // Default Interface Method
}

public interface IRelationalOutboxTransactionContext : IOutboxTransactionContext
{
    DbConnection? DbConnection { get; }
    DbTransaction? DbTransaction { get; }
}

// Built-in implementation for ADO.NET transactions
public sealed class DbTransactionContext : IRelationalOutboxTransactionContext
{
    public DbTransactionContext(DbTransaction dbTransaction);
}
```

---

## 7. Persistence — Data Models

### `OutboxMessage`

**Namespace:** `EricksonLopez.Outbox`
**Type:** `readonly record struct` (stack-allocated, value equality)

| Property | Type | Description |
|---|---|---|
| `Id` | `Guid` | Unique message identifier |
| `Type` | `string` | Message type alias (from `[OutboxMessage]`) |
| `Payload` | `ReadOnlyMemory<byte>` | Serialized message body |
| `State` | `int` | 0=Pending, 1=InFlight, 2=Dispatched, 3=Failed, 4=DeadLettered |
| `RetryCount` | `int` | Number of dispatch attempts |
| `Error` | `string?` | Last error message (truncated by `IErrorSanitizer`) |
| `CreatedAt` | `DateTimeOffset` | Creation timestamp |
| `DeliverAt` | `DateTimeOffset?` | Scheduled delivery time |

### `MessageMetadata`

| Property | Type | Description |
|---|---|---|
| `CorrelationId` | `string?` | Distributed tracing correlation ID |
| `CausationId` | `string?` | ID of the command/event that caused this message |

### `DeadLetterMessage`

| Property | Type | Description |
|---|---|---|
| `Id` | `Guid` | Original message ID |
| `Type` | `string` | Message type alias |
| `Payload` | `ReadOnlyMemory<byte>` | Serialized message body |
| `Error` | `string?` | Last error that caused dead-lettering |
| `DeadLetteredAt` | `DateTimeOffset` | Timestamp of dead-lettering |
| `RetryCount` | `int` | Total retry attempts made |

### `IdempotencyRecord`

| Property | Type | Description |
|---|---|---|
| `MessageId` | `string` | Unique identifier of the processed message |
| `ConsumerId` | `string` | Identifier of the consumer that processed it |
| `ProcessedAt` | `DateTimeOffset` | When the record was created |

---

## 8. Configuration & DI

### `OutboxServiceCollectionExtensions`

**Namespace:** `EricksonLopez.Outbox.Hosting`
**Source:** [`OutboxServiceCollectionExtensions.cs`](../src/EricksonLopez.Outbox/Hosting/OutboxServiceCollectionExtensions.cs)

```csharp
public static class OutboxServiceCollectionExtensions
{
    // Register core Outbox services (IOutbox, serialization, options)
    static IServiceCollection AddOutbox(
        this IServiceCollection services,
        Action<OutboxOptions>? configure = null);

    // Register the background dispatcher service
    static IServiceCollection AddOutboxDispatcher(
        this IServiceCollection services,
        Action<OutboxDispatcherOptions>? configure = null);

    // Register Inbox idempotency services
    static IServiceCollection AddOutboxInbox(
        this IServiceCollection services,
        Action<OutboxInboxOptions>? configure = null);
}

public static class OutboxHealthCheckExtensions
{
    static IHealthChecksBuilder AddOutbox(
        this IHealthChecksBuilder builder,
        string name = "outbox",
        int? warningThreshold = null,
        params string[] tags);
}
```

### `OutboxOptions`

```csharp
public class OutboxOptions
{
    OutboxOptions UseSerializer(IOutboxSerializer serializer);
    OutboxOptions UseSerializer<TSerializer>() where TSerializer : class, IOutboxSerializer;
    OutboxOptions UseGeneratedTypes();
    OutboxOptions UseBroker(Func<IServiceProvider, IBrokerPublisher> factory);
    OutboxOptions UseBroker(string route, Func<IServiceProvider, IBrokerPublisher> factory);
    OutboxOptions UseBroker<TBroker>(string? route = null) where TBroker : class, IBrokerPublisher;
}
```

### `OutboxDispatcherOptions`

| Property | Type | Default | Description |
|---|---|---|---|
| `BatchSize` | `int` | `50` | Messages per polling cycle |
| `PollingInterval` | `TimeSpan` | `1s` | Base polling interval |
| `MaxDegreeOfParallelism` | `int` | `1` | Concurrent dispatch workers |
| `UseAdaptivePolling` | `bool` | `true` | Dynamic polling frequency |
| `MaxRetryCount` | `int` | `3` | Retries before dead-lettering |

### EF Core Registration

**Package:** `EricksonLopez.Outbox.EntityFrameworkCore`

```csharp
public static class OutboxEntityFrameworkCoreSetup
{
    static IServiceCollection AddOutboxEntityFrameworkCore<TDbContext>(
        this IServiceCollection services) where TDbContext : DbContext;
}

public static class OutboxModelBuilderExtensions
{
    static ModelBuilder ApplyOutboxEntityConfigurations(this ModelBuilder builder);
}
```

---

## 9. Diagnostics

### `OutboxMetrics`

**Namespace:** `EricksonLopez.Outbox.Diagnostics`

Emits `System.Diagnostics.Metrics` counters under the `EricksonLopez.Outbox` meter.

| Instrument | Type | Description |
|---|---|---|
| `outbox.messages.stored` | Counter | Messages stored via `StoreAsync()` |
| `outbox.messages.dispatched` | Counter | Messages successfully published |
| `outbox.messages.failed` | Counter | Failed dispatch attempts |
| `outbox.messages.dead_lettered` | Counter | Messages moved to DLQ |

### `OutboxActivitySource`

Tracing source name: `EricksonLopez.Outbox`

Register with OpenTelemetry:
```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource("EricksonLopez.Outbox"));
```

### `IErrorSanitizer`

```csharp
public interface IErrorSanitizer
{
    string Sanitize(string? error);
}
```

**Default:** `DefaultErrorSanitizer` — truncates to 4000 characters.

---

## 10. Roslyn Tooling

### Analyzers (`EricksonLopez.Outbox.Analyzers`)

| Diagnostic ID | Severity | Description |
|---|---|---|
| `OUTBOX001` | Warning | Message type missing `[OutboxMessage]` attribute |
| `OUTBOX002` | Warning | `StoreAsync()` called outside transaction context |

### Source Generator (`EricksonLopez.Outbox.SourceGenerators`)

**`OutboxTypeMappingGenerator`** — Incremental source generator that:
1. Discovers all types decorated with `[OutboxMessage("alias")]`
2. Emits `[assembly: OutboxTypeMapping("alias", typeof(T))]` registration
3. Emits a commented `JsonSerializerContext` template for NativeAOT

### `[OutboxMessage]` Attribute

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class OutboxMessageAttribute : Attribute
{
    public OutboxMessageAttribute(string alias);
    public string Alias { get; }
}
```
