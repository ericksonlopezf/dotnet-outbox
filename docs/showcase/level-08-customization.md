# Level 8: Customization and Extensibility

This level covers the extension points in `EricksonLopez.Outbox` — custom serializers, custom brokers, typed dispatch, middleware pipelines, and error sanitization.

## 1. Custom Serializer (`IOutboxSerializer`)

The `IOutboxSerializer` interface controls how message payloads are serialized to and deserialized from the outbox table. It uses `ReadOnlyMemory<byte>` and `IBufferWriter<byte>` to avoid allocations on the hot path.

```csharp
using EricksonLopez.Outbox.Serialization;
using System.Buffers;

public interface IOutboxSerializer
{
    // Returns a ReadOnlyMemory<byte> — avoids defensive array copying
    ReadOnlyMemory<byte> Serialize<TMessage>(TMessage message);

    // Zero-allocation path: write directly into the IBufferWriter<byte>
    // The default implementation delegates to Serialize<T>(); override for max throughput.
    void Serialize<TMessage>(TMessage message, IBufferWriter<byte> buffer);

    // Deserialize from a ReadOnlySpan<byte> (stack-based, no heap allocation)
    TMessage Deserialize<TMessage>(ReadOnlySpan<byte> data);
}
```

### Built-in: `NativeAotJsonSerializer`

The default serializer uses `System.Text.Json` with source-generated `JsonSerializerContext` for zero-reflection, AOT-compatible serialization:

```csharp
options.UseSerializer(new NativeAotJsonSerializer(MyJsonContext.Default));
```

### Custom Implementation Example (Protobuf)

```csharp
public class ProtobufOutboxSerializer : IOutboxSerializer
{
    public ReadOnlyMemory<byte> Serialize<TMessage>(TMessage message)
    {
        using var ms = new MemoryStream();
        ProtoBuf.Serializer.Serialize(ms, message);
        return ms.ToArray();
    }

    public void Serialize<TMessage>(TMessage message, IBufferWriter<byte> buffer)
    {
        // For zero-allocation: write directly if ProtoBuf supports IBufferWriter
        var bytes = Serialize(message);
        var span = buffer.GetSpan(bytes.Length);
        bytes.Span.CopyTo(span);
        buffer.Advance(bytes.Length);
    }

    public TMessage Deserialize<TMessage>(ReadOnlySpan<byte> data)
        => ProtoBuf.Serializer.Deserialize<TMessage>(new ReadOnlySequence<byte>(data.ToArray()));
}
```

---

## 2. Custom Broker Publisher (`IBrokerPublisher` & `ITypedBrokerPublisher`)

Implement `IBrokerPublisher` to integrate with any messaging system. The raw publisher receives the pre-serialized `OutboxMessage` bytes directly — the fastest possible integration path.

### `IBrokerPublisher` (Raw Bytes)

```csharp
using EricksonLopez.Outbox;

public interface IBrokerPublisher
{
    ValueTask<DispatchResult> PublishRawAsync(
        OutboxMessage message,    // Contains pre-serialized Payload (byte[]) and MessageType alias
        MessageMetadata metadata, // CorrelationId, CausationId, MessageType alias
        DispatchContext context); // CancellationToken + Attempt number
}
```

Implementation example:

```csharp
public sealed class RabbitMqBrokerPublisher : IBrokerPublisher
{
    private readonly IConnection _connection;
    private readonly CircuitBreakerState _circuitBreaker = new(failureThreshold: 5);

    public RabbitMqBrokerPublisher(IConnection connection) => _connection = connection;

    // Override the OTel messaging.system tag for this broker:
    public string BrokerSystemName => "rabbitmq";

    public async ValueTask<DispatchResult> PublishRawAsync(
        OutboxMessage message,
        MessageMetadata metadata,
        DispatchContext context)
    {
        if (!_circuitBreaker.AllowRequest())
            return DispatchResult.FailFatal("Circuit breaker is Open."); // Fatal → no retry

        try
        {
            using var channel = await _connection.CreateChannelAsync();

            var props = new BasicProperties
            {
                MessageId = message.Id.ToString(),
                CorrelationId = metadata.CorrelationId,
                ContentType = "application/json"
            };

            await channel.BasicPublishAsync(
                exchange: message.MessageType, // Use alias as routing key
                routingKey: string.Empty,
                mandatory: false,
                basicProperties: props,
                body: message.Payload,
                cancellationToken: context.CancellationToken);

            _circuitBreaker.RecordSuccess();
            return DispatchResult.Ok();                       // ✅ Correct factory method
        }
        catch (NetworkException ex)                           // Transient → retry
        {
            _circuitBreaker.RecordFailure();
            return DispatchResult.FailAndRetry(ex);           // ✅ Correct: schedules retry
        }
        catch (AuthenticationException ex)                    // Fatal → DLQ immediately
        {
            _circuitBreaker.RecordFailure();
            return DispatchResult.FailFatal(ex);              // ✅ Correct: no retry, dead-letter
        }
        catch (Exception ex)                                  // Unknown → retry conservatively
        {
            _circuitBreaker.RecordFailure();
            return DispatchResult.FailAndRetry(ex);
        }
    }
}
```

> [!WARNING]
> **Common mistake**: Using `DispatchResult.Success()` or `DispatchResult.Failure()` — these methods do **not exist**. Always use:
> - `DispatchResult.Ok()` for success
> - `DispatchResult.FailAndRetry(Exception)` for transient failures (retryable)
> - `DispatchResult.FailFatal(Exception)` for permanent failures (dead-letter immediately)
> - `DispatchResult.FailFatal(string reason)` for permanent failures with a string reason
> - `DispatchResult.FailFatal(Guid messageId, int retryCount, string reason)` with full context
> - `DispatchResult.FailAndRetry(Exception, bool incrementRetryCount)` to control retry counter

### `DispatchResult` — Complete Factory Method Reference

```csharp
// SUCCESS
DispatchResult.Ok();
    // Success=true, ShouldRetry=false. Use after successful publish.

// TRANSIENT FAILURES (retriable)
DispatchResult.FailAndRetry(Exception ex);
    // ShouldRetry=true, IncrementRetryCount=true.
    // Use for: network timeouts, broker unavailable, rate limited.

DispatchResult.FailAndRetry(Exception ex, bool incrementRetryCount);
    // ShouldRetry=true, IncrementRetryCount=incrementRetryCount.
    // Use when you want to retry but not count against the MaxRetryCount limit.

// FATAL FAILURES (no retry, dead-letter immediately)
DispatchResult.FailFatal(Exception ex);
    // ShouldRetry=false. Use for: serialization failure, schema mismatch, auth denied.

DispatchResult.FailFatal(string reason);
    // Convenience overload. Internally wraps in OutboxDispatchException(Guid.Empty, 0, reason).

DispatchResult.FailFatal(Guid messageId, int retryCount, string reason);
    // Full-context fatal failure with message ID and attempt number.

// VALIDATION
result.ThrowIfInvalid();
    // Throws InvalidOperationException if Success=true && ShouldRetry=true (invalid state).
    // Also throws if Success=false && Error=null (failed result must have an attached error).
```

> [!IMPORTANT]
> **Do NOT return `default(DispatchResult)`**. The default value (`Success=false, ShouldRetry=false, Error=null`) is an invalid state that causes the dispatcher to dead-letter the message with a misleading "no error" state. Always use one of the factory methods above.

### `ITypedBrokerPublisher` (Deserialized CLR Objects)

If your broker library requires strongly-typed CLR objects (e.g., MassTransit, Wolverine), implement `ITypedBrokerPublisher`. The dispatcher detects this interface and deserializes the payload before calling `PublishAsync<T>`:

```csharp
using EricksonLopez.Outbox;

public interface ITypedBrokerPublisher : IBrokerPublisher
{
    // Single typed message — dispatcher deserializes before calling this
    ValueTask<DispatchResult> PublishAsync<T>(
        MessageEnvelope<T> envelope,  // Wraps the deserialized payload + metadata
        DispatchContext context)
        where T : notnull;

    // Batch typed messages for high-throughput transactional publishing
    ValueTask<IReadOnlyList<DispatchResult>> PublishBatchAsync<T>(
        IReadOnlyList<MessageEnvelope<T>> envelopes,
        DispatchContext context)
        where T : notnull;
}
```

> [!NOTE]
> `ITypedBrokerPublisher` extends `IBrokerPublisher`. When the dispatcher encounters a publisher that implements `ITypedBrokerPublisher`, it uses `PublishAsync<T>` (via reflection-free generic dispatch) instead of `PublishRawAsync`. This means the raw bytes are deserialized back into a CLR object before forwarding to the broker library.
> 
> **Choose raw (`IBrokerPublisher`)** when your broker adapter can work with `ReadOnlyMemory<byte>` directly — zero overhead, NativeAOT-safe.  
> **Choose typed (`ITypedBrokerPublisher`)** when the broker client library requires a typed object (e.g., `IBus.Publish<T>(message)` in MassTransit).

### `MessageEnvelope<T>` Structure

```csharp
// MessageEnvelope<T> is a struct — no heap allocation
var envelope = new MessageEnvelope<OrderCreatedEvent>(
    payload: orderCreatedEvent,
    metadata: new MessageMetadata(correlationId, causationId, "order-created-v1"),
    message: outboxMessage);  // The raw OutboxMessage with headers, ID, etc.
```

### Registering Custom Publishers

```csharp
builder.Services.AddOutbox(options =>
{
    // Default publisher (used for all aliases not explicitly routed)
    options.UseBroker(sp => new RabbitMqBrokerPublisher(sp.GetRequiredService<IConnection>()));

    // Or by type (resolved from DI):
    options.UseBroker<RabbitMqBrokerPublisher>();

    // Route-specific publisher via alias:
    options.Route("analytics-event-v1")
           .ToPublisher(sp => new KafkaBrokerPublisher(sp.GetRequiredService<IProducer<string, byte[]>>()));

    // Route-specific publisher via pre-built instance:
    options.Route("notification-sent-v1")
           .ToPublisher(new AwsSqsBrokerPublisher(sqsClient, queueUrl));
});
```

---

## 3. Error Sanitization (`IErrorSanitizer`)

Before exceptions are stored in the `last_error` column of the DLQ or logged, you can scrub them to prevent PII or credentials from leaking into your persistence layer.

```csharp
using EricksonLopez.Outbox.Diagnostics;

public sealed class CustomErrorSanitizer : IErrorSanitizer
{
    public string Sanitize(Exception exception)
    {
        if (exception.Message.Contains("Password=", StringComparison.OrdinalIgnoreCase))
            return "A database error occurred (connection details redacted).";

        var text = exception.ToString();
        return text.Length > 4000 ? text[..4000] : text;
    }
}

// Register as Singleton:
services.AddSingleton<IErrorSanitizer, CustomErrorSanitizer>();
```

---

## 4. Middleware Pipeline (`IOutboxMiddleware` & `OutboxPipeline`)

The outbox supports an ASP.NET Core-style middleware pipeline that intercepts **every dispatch operation**. Each middleware receives the `OutboxMessage`, its `MessageMetadata`, and a delegate to the `next` step in the chain.

### `IOutboxMiddleware` Interface

```csharp
using EricksonLopez.Outbox.Pipeline;
using EricksonLopez.Outbox;

public interface IOutboxMiddleware
{
    ValueTask<DispatchResult> InvokeAsync(
        OutboxMessage message,          // The pre-serialized outbox message (Id, Payload, MessageType, ...)
        MessageMetadata metadata,       // Routing metadata (CorrelationId, CausationId, MessageType alias)
        OutboxPipelineDelegate next,    // Delegate to the next middleware in the chain
        CancellationToken cancellationToken);
}

// OutboxPipelineDelegate — the delegate type that chains middleware steps:
public delegate ValueTask<DispatchResult> OutboxPipelineDelegate(
    OutboxMessage message,
    MessageMetadata metadata,
    CancellationToken cancellationToken);
```

### Example: Logging Middleware

```csharp
public sealed class LoggingMiddleware : IOutboxMiddleware
{
    private readonly ILogger<LoggingMiddleware> _logger;

    public LoggingMiddleware(ILogger<LoggingMiddleware> logger) => _logger = logger;

    public async ValueTask<DispatchResult> InvokeAsync(
        OutboxMessage message,
        MessageMetadata metadata,
        OutboxPipelineDelegate next,
        CancellationToken ct)
    {
        _logger.LogDebug(
            "Dispatching {MessageType} (Id={MessageId})",
            message.MessageType, message.Id);

        var result = await next(message, metadata, ct);

        if (result.Success)
            _logger.LogDebug("Dispatched {MessageType} successfully.", message.MessageType);
        else
            _logger.LogWarning("Dispatch failed for {MessageType}: {Error}", message.MessageType, result.Error);

        return result;
    }
}
```

### Example: Message Filter Middleware (Short-Circuit)

```csharp
public sealed class MessageFilterMiddleware : IOutboxMiddleware
{
    private readonly HashSet<string> _blockedAliases;

    public MessageFilterMiddleware(IConfiguration config)
    {
        _blockedAliases = new HashSet<string>(
            config.GetSection("Outbox:BlockedAliases").Get<string[]>() ?? [],
            StringComparer.OrdinalIgnoreCase);
    }

    public ValueTask<DispatchResult> InvokeAsync(
        OutboxMessage message,
        MessageMetadata metadata,
        OutboxPipelineDelegate next,
        CancellationToken ct)
    {
        if (_blockedAliases.Contains(message.MessageType))
        {
            // Short-circuit: return success without calling next
            return ValueTask.FromResult(DispatchResult.Ok()); // ✅ Correct: DispatchResult.Ok()
        }

        return next(message, metadata, ct); // Pass through to the next middleware
    }
}
```

### Registering Middleware

Middleware is registered in DI as `IOutboxMiddleware`. **Registration order = execution order in the pipeline**.

```csharp
// Registration order determines execution order
builder.Services.AddSingleton<IOutboxMiddleware, LoggingMiddleware>();     // Runs 1st
builder.Services.AddSingleton<IOutboxMiddleware, MessageFilterMiddleware>(); // Runs 2nd
builder.Services.AddSingleton<IOutboxMiddleware, HeaderEnrichmentMiddleware>(); // Runs 3rd

// Optimization: if ALL middlewares are Singleton, enable pipeline caching:
builder.Services.AddOutboxDispatcher(options =>
{
    options.HasOnlySingletonMiddlewares = true; // Eliminates allocations per batch
});
```

### `OutboxPipeline` — Building a Custom Pipeline

The `OutboxPipeline` sealed class compiles middleware into an immutable delegate chain at construction time — the same pattern used internally by the dispatcher:

```csharp
using EricksonLopez.Outbox.Pipeline;

// Build a custom pipeline for a specific scenario:
var pipeline = new OutboxPipeline(
    middlewares: new IOutboxMiddleware[] { new LoggingMiddleware(logger), new FilterMiddleware() },
    terminal: (message, metadata, ct) => publisher.PublishRawAsync(message, metadata, context));

// Execute it:
var result = await pipeline.ExecuteAsync(outboxMessage, messageMetadata, ct);
```

---

## 5. Custom Message Type Resolver

If you cannot use source generators, you can manually build the alias → CLR type map:

```csharp
using EricksonLopez.Outbox.Serialization;

var resolver = new InMemoryMessageTypeResolver(new[]
{
    ("order-created-v1", typeof(OrderCreatedEvent)),
    ("order-confirmed-v1", typeof(OrderConfirmedEvent)),
    ("user-registered-v1", typeof(UserRegisteredEvent)),
});

options.UseTypeResolver(resolver);
```

### `InMemoryMessageTypeResolver` API

| Member | Description |
|---|---|
| `Type? Resolve(string alias)` | Alias → CLR type. Returns `null` if not found. |
| `bool TryGetAlias(Type, out string? alias)` | CLR type → alias. Returns `false` if not registered. |
| `bool TryGetAlias<TMessage>(out string? alias)` | Generic overload. JIT specializes per type for zero-overhead. |
| `string GetAlias(Type)` | CLR type → alias. Throws `InvalidOperationException` if not found. |
| `string GetAlias<TMessage>()` | Generic overload of `GetAlias`. |
| `IReadOnlyDictionary<string, Type> GetAllMappings()` | Returns all registered alias → CLR type pairs (uses `FrozenDictionary`). |

---

## 6. `OutboxConstants` — Reserved Identifiers

`OutboxConstants` exposes reserved string constants used internally by the library. You can reference them to avoid hardcoding magic strings:

```csharp
using EricksonLopez.Outbox;

// The consumer ID reserved for the outbox dispatcher's internal deduplication.
// DO NOT use this as your own consumerId in IInboxIdempotencyChecker calls.
string reservedId = OutboxConstants.DispatcherConsumerId; // "outbox-dispatcher"

// Correct: always use a unique, service-scoped consumer ID:
private const string MyConsumerId = "my-service.my-handler"; // ✅
// private const string BadId = OutboxConstants.DispatcherConsumerId; // ❌ collision risk
```

### `IInboxIdempotencyChecker` — Full API

The idempotency checker has two methods with distinct use cases:

```csharp
using EricksonLopez.Outbox.Idempotency;
using EricksonLopez.Outbox.Persistence;

// ShouldProcessAsync — recommended for most consumers:
// Atomically inserts (messageId, consumerId) using ON CONFLICT DO NOTHING.
// Returns true if the record was inserted (=first time seen).
// Returns false if the record already exists (=duplicate, skip).
bool shouldProcess = await _inbox.ShouldProcessAsync(
    messageId: evt.MessageId,
    consumerId: "billing-service.order-handler",
    transaction: txContext,
    cancellationToken: ct);

if (!shouldProcess) return; // Duplicate — safe to ignore

// ShouldSkipAsync — alternative for when you check BEFORE starting a transaction:
// Returns true if the message was already processed (should skip).
// Returns false if the message is new (should process).
bool alreadyProcessed = await _inbox.ShouldSkipAsync(
    messageId: Guid.Parse(evt.MessageId),
    transaction: txContext,
    consumerId: "billing-service.order-handler",
    cancellationToken: ct);

if (alreadyProcessed) return; // Already done — skip
```

| Method | Signature | Returns `true` when... |
|---|---|---|
| `ShouldProcessAsync` | `(string messageId, string consumerId, IOutboxTransactionContext, ct)` | Record **was inserted** (new message — proceed) |
| `ShouldSkipAsync` | `(Guid messageId, IOutboxTransactionContext, string consumerId, ct)` | Record **already exists** (duplicate — skip) |

> [!CAUTION]
> **`ShouldProcessAsync`** and **`ShouldSkipAsync`** have **opposite return semantics**:
> - `ShouldProcessAsync` → `true` = **process** the message
> - `ShouldSkipAsync` → `true` = **skip** the message (it was already processed)
>
> Choose one style and use it consistently throughout your codebase to avoid logic inversions.

---

**Next:** In [Level 9](level-09-extensions.md), you will explore framework integrations: Entity Framework Core, MassTransit, and more.
