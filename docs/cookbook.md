<!-- Copyright © Erickson Lopez. MIT License. -->

# Transactional Outbox & Inbox Cookbook

Production-grade recipes and architectural patterns for `EricksonLopez.Outbox` and `EricksonLopez.Inbox`.

---

## Recipes Index

1. [Recipe 1: Atomic Transaction with Raw ADO.NET (PostgreSQL)](#recipe-1-atomic-transaction-with-raw-adonet-postgresql)
2. [Recipe 2: Atomic Transaction with Entity Framework Core](#recipe-2-atomic-transaction-with-entity-framework-core)
3. [Recipe 3: High-Throughput Zero-Allocation Batching](#recipe-3-high-throughput-zero-allocation-batching)
4. [Recipe 4: Clean Architecture &amp; Domain Events via Interceptor](#recipe-4-clean-architecture--domain-events-via-interceptor)
5. [Recipe 5: Scheduled &amp; Delayed Message Delivery](#recipe-5-scheduled--delayed-message-delivery)
6. [Recipe 6: Idempotent Consumer &amp; Inbox Deduplication](#recipe-6-idempotent-consumer--inbox-deduplication)
7. [Recipe 7: ASP.NET Core HTTP `Idempotency-Key` Endpoint Filter](#recipe-7-aspnet-core-http-idempotency-key-endpoint-filter)
8. [Recipe 8: Custom Dispatch Middleware (Telemetry &amp; Enrichment)](#recipe-8-custom-dispatch-middleware-telemetry--enrichment)
9. [Recipe 9: Serverless &amp; Cron On-Demand Dispatching (`ManualOutboxDispatcher`)](#recipe-9-serverless--cron-on-demand-dispatching-manualoutboxdispatcher)
10. [Recipe 10: Unit Testing Without a Database (`InMemoryOutboxStore`)](#recipe-10-unit-testing-without-a-database-inmemoryoutboxstore)
11. [Recipe 11: Exception-Aware Custom `IRetryPolicy`](#recipe-11-exception-aware-custom-iretrrypolicy)
12. [Recipe 12: Dead Letter Queue Administration (Purge + Crash Recovery)](#recipe-12-dead-letter-queue-administration-purge--crash-recovery)
13. [Recipe 13: Implementing a Custom `IBrokerPublisher` with Correct `DispatchResult` Mapping](#recipe-13-implementing-a-custom-ibrokerpublisher-with-correct-dispatchresult-mapping)

---

## Recipe 1: Atomic Transaction with Raw ADO.NET (PostgreSQL)

### Problem
You need to insert a business entity using raw SQL (`Npgsql`) and guarantee that an integration event is saved in the outbox within the exact same database transaction.

### Solution
Wrap the active `NpgsqlTransaction` using `tx.ToOutboxContext()` and call `IOutbox.StoreAsync`.

### Code
```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Contracts;
using EricksonLopez.Outbox.Persistence;
using Npgsql;

[OutboxMessage("order.created.v1")]
public sealed record OrderCreatedEvent(Guid OrderId, string CustomerId, decimal Amount, DateTimeOffset CreatedAt);

public sealed class OrderRepository
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IOutbox _outbox;

    public OrderRepository(NpgsqlDataSource dataSource, IOutbox outbox)
    {
        _dataSource = dataSource;
        _outbox = outbox;
    }

    public async Task CreateOrderAsync(Guid orderId, string customerId, decimal amount, CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);

        // 1. Domain persistence
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO orders (id, customer_id, total, created_at) VALUES (@id, @cid, @tot, @cat)", 
            conn, tx);
        cmd.Parameters.AddWithValue("id", orderId);
        cmd.Parameters.AddWithValue("cid", customerId);
        cmd.Parameters.AddWithValue("tot", amount);
        cmd.Parameters.AddWithValue("cat", DateTimeOffset.UtcNow);
        await cmd.ExecuteNonQueryAsync(ct);

        // 2. Outbox persistence (same transaction context)
        var @event = new OrderCreatedEvent(orderId, customerId, amount, DateTimeOffset.UtcNow);
        await _outbox.StoreAsync(@event, tx.ToOutboxContext(), ct);

        // 3. Unified atomic commit
        await tx.CommitAsync(ct);
    }
}
```

### Best Practices
- Always call `tx.CommitAsync()` after `outbox.StoreAsync()`.
- Use `tx.ToOutboxContext()` extension method to avoid manual wrapper instantiation.

### Common Errors
- ❌ Committing the transaction *before* calling `StoreAsync()`.
- ❌ Opening a second database connection for the outbox.

---

## Recipe 2: Atomic Transaction with Entity Framework Core

### Problem
You use EF Core for your aggregate root persistence and want to store an outbox message within the EF Core transaction boundary.

### Solution
Begin an EF Core transaction via `dbContext.Database.BeginTransactionAsync()`, extract the underlying transaction using `DbTransactionContext`, store the outbox message, and commit.

### Code
```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Persistence;

public sealed class OrderCommandHandler
{
    private readonly AppDbContext _dbContext;
    private readonly IOutbox _outbox;

    public OrderCommandHandler(AppDbContext dbContext, IOutbox outbox)
    {
        _dbContext = dbContext;
        _outbox = outbox;
    }

    public async Task HandleCreateOrderAsync(string customerId, decimal total, CancellationToken ct)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(ct);

        // 1. Persist Domain Entities
        var order = new Order { Id = Guid.NewGuid(), CustomerId = customerId, Total = total };
        _dbContext.Orders.Add(order);
        await _dbContext.SaveChangesAsync(ct);

        // 2. Store Outbox Message in the same EF Core transaction
        var dbTransaction = transaction.GetDbTransaction();
        var @event = new OrderCreatedEvent(order.Id, customerId, total, DateTimeOffset.UtcNow);
        await _outbox.StoreAsync(@event, new DbTransactionContext(dbTransaction), ct);

        // 3. Commit EF Core transaction
        await transaction.CommitAsync(ct);
    }
}
```

---

## Recipe 3: High-Throughput Zero-Allocation Batching

### Problem
You are processing high-volume ingestion (e.g. bulk CSV import or IoT sensor readings) and need to insert 5,000 outbox records per second without GC pressure.

### Solution
Use `IOutbox.StoreAsync(ReadOnlyMemory<TMessage>, ...)` with contiguous arrays or pooled memory slices.

### Code
```csharp
using System;
using System.Buffers;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Persistence;
using Npgsql;

public async Task IngestSensorReadingsAsync(
    SensorReading[] readings, 
    int count, 
    NpgsqlDataSource dataSource, 
    IOutbox outbox, 
    CancellationToken ct)
{
    await using var conn = await dataSource.OpenConnectionAsync(ct);
    await using var tx = await conn.BeginTransactionAsync(ct);

    // Pass ReadOnlyMemory slice directly — zero array copies
    var slice = new ReadOnlyMemory<SensorReading>(readings, 0, count);
    await outbox.StoreAsync(slice, tx.ToOutboxContext(), ct);

    await tx.CommitAsync(ct);
}
```

---

## Recipe 4: Clean Architecture & Domain Events via Interceptor

### Problem
You follow Domain-Driven Design (DDD) where domain aggregates raise domain events, and you want to persist these events to the outbox automatically whenever EF Core saves changes.

### Solution
Implement an EF Core `SaveChangesInterceptor` that drains domain events from aggregates and calls `IOutbox.StoreAsync` within the active transaction.

### Code
```csharp
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Persistence;

public sealed class OutboxDomainEventsInterceptor : SaveChangesInterceptor
{
    private readonly IOutbox _outbox;

    public OutboxDomainEventsInterceptor(IOutbox outbox)
    {
        _outbox = outbox;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        var context = eventData.Context;
        if (context == null) return result;

        var entitiesWithEvents = context.ChangeTracker.Entries<IHasDomainEvents>()
            .Select(e => e.Entity)
            .Where(e => e.DomainEvents.Any())
            .ToList();

        if (entitiesWithEvents.Count == 0) return result;

        var currentTransaction = context.Database.CurrentTransaction?.GetDbTransaction();
        var transactionContext = currentTransaction != null ? new DbTransactionContext(currentTransaction) : null;

        foreach (var entity in entitiesWithEvents)
        {
            foreach (var domainEvent in entity.DomainEvents)
            {
                await _outbox.StoreAsync(domainEvent, transactionContext, cancellationToken);
            }
            entity.ClearDomainEvents();
        }

        return result;
    }
}
```

---

## Recipe 5: Scheduled & Delayed Message Delivery

### Problem
You need to publish an event that becomes visible for broker dispatch only after a specific delay (e.g. reminder email in 24 hours, invoice expiration check).

### Solution
Use `outbox.Publish(event).WithDelay(TimeSpan)` or `.DeliverAt(DateTimeOffset)`.

### Code
```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Persistence;

public sealed class PaymentService
{
    private readonly IOutbox _outbox;

    public PaymentService(IOutbox outbox)
    {
        _outbox = outbox;
    }

    public async Task SchedulePaymentReminderAsync(
        Guid invoiceId, 
        IOutboxTransactionContext transaction, 
        CancellationToken ct)
    {
        var reminderEvent = new InvoicePaymentReminderDue(invoiceId, DateTimeOffset.UtcNow);

        // Schedule message delivery 24 hours in the future
        await _outbox.Publish(reminderEvent)
            .WithTransaction(transaction)
            .WithDelay(TimeSpan.FromHours(24))
            .StoreAsync(ct);
    }
}
```

---

## Recipe 6: Idempotent Consumer & Inbox Deduplication

### Problem
A message broker delivers a duplicate message due to network reconnection (At-Least-Once delivery). The consumer must detect duplicates and avoid processing business logic twice.

### Solution
Use `IInboxIdempotencyChecker.ShouldProcessAsync()` within the consumer's local database transaction.

### Code
```csharp
using System;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox.Idempotency;
using EricksonLopez.Outbox.Persistence;
using Npgsql;

public sealed class PaymentProcessedConsumer
{
    private readonly NpgsqlDataSource _dataSource;
    private readonly IInboxIdempotencyChecker _idempotencyChecker;

    public PaymentProcessedConsumer(NpgsqlDataSource dataSource, IInboxIdempotencyChecker idempotencyChecker)
    {
        _dataSource = dataSource;
        _idempotencyChecker = idempotencyChecker;
    }

    public async Task ConsumeAsync(string messageId, Guid orderId, decimal amount, CancellationToken ct)
    {
        await using var conn = await _dataSource.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        var dbTx = tx.ToOutboxContext();

        // 1. Atomic Inbox idempotency check
        var shouldProcess = await _idempotencyChecker.ShouldProcessAsync(
            messageId: messageId,
            consumerId: "BillingService.PaymentConsumer",
            transaction: dbTx,
            cancellationToken: ct);

        if (!shouldProcess)
        {
            // Duplicate detected! Safe to ACK without re-executing business logic.
            await tx.RollbackAsync(ct);
            return;
        }

        // 2. Execute business operations
        await using var cmd = new NpgsqlCommand("UPDATE orders SET status = 'Paid' WHERE id = @id", conn, tx);
        cmd.Parameters.AddWithValue("id", orderId);
        await cmd.ExecuteNonQueryAsync(ct);

        // 3. Atomic commit (both business update and idempotency record committed together)
        await tx.CommitAsync(ct);
    }
}
```

---

## Recipe 7: ASP.NET Core HTTP `Idempotency-Key` Endpoint Filter

### Problem
An API client retries a `POST /api/orders` payment request due to a client-side timeout. The API must return the original cached response without creating a duplicate order.

### Solution
Apply `IdempotentEndpointFilter` to the Minimal API route using `.RequireIdempotency()`.

### Code
```csharp
using EricksonLopez.Outbox.Inbox.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

var builder = WebApplication.CreateBuilder(args);

// Register Inbox Deduplication
builder.Services.AddInboxDeduplication();

var app = builder.Build();

app.MapPost("/api/checkout", async (CheckoutRequest request) =>
{
    // Process business transaction...
    return Results.Ok(new { status = "Confirmed", orderId = Guid.NewGuid() });
})
.RequireIdempotency(); // Intercepts HTTP Idempotency-Key header

app.Run();
```

---

## Recipe 8: Custom Dispatch Middleware (Telemetry & Enrichment)

### Problem
You need to inject custom tenant headers, telemetry baggage, or security tokens into every outbox message right before it is published to the broker.

### Solution
Implement `IOutboxMiddleware` and register it in the outbox pipeline.

### Code
```csharp
using System.Threading.Tasks;
using EricksonLopez.Outbox.Pipeline;

public sealed class CorrelationEnrichmentMiddleware : IOutboxMiddleware
{
    public async ValueTask InvokeAsync(
        OutboxMessage message,
        OutboxMessageMetadata metadata,
        DispatchContext context,
        OutboxPipelineDelegate next)
    {
        // Enrich context or propagate correlation headers
        context.Headers["x-app-version"] = "1.0.0";
        context.Headers["x-environment"] = "Production";

        await next(message, metadata, context);
    }
}

// Registration:
services.AddOutboxServices(builder =>
{
    builder.UseMiddleware<CorrelationEnrichmentMiddleware>();
});
```

---

## Recipe 9: Serverless & Cron On-Demand Dispatching (`ManualOutboxDispatcher`)

### Problem
You run in a serverless environment (AWS Lambda, Azure Functions) or want a cron job to drain pending outbox messages on demand rather than running a 24/7 background worker daemon.

### Solution
Use `IManualOutboxDispatcher.DrainBatchAsync()` to fetch and dispatch a fixed batch of pending messages.

### Code
```csharp
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox.Hosting;
using Microsoft.Azure.Functions.Worker;

public sealed class OutboxDrainFunction
{
    private readonly IManualOutboxDispatcher _dispatcher;

    public OutboxDrainFunction(IManualOutboxDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    [Function("DrainOutboxTimer")]
    public async Task Run([TimerTrigger("*/30 * * * * *")] TimerInfo timer, CancellationToken ct)
    {
        // Drain up to 500 pending messages on demand
        var dispatchedCount = await _dispatcher.DrainBatchAsync(maxBatchSize: 500, ct);
    }
}
```

---

## Recipe 10: Unit Testing Without a Database (`InMemoryOutboxStore`)

### Problem
You want to write fast unit and component tests verifying that your domain command handlers store the expected outbox messages, without spinning up Docker or real databases.

### Solution
Use `TestingOutboxExtensions` with `InMemoryOutboxStore` and `FakeBrokerPublisher`.

### Code
```csharp
using System;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox.Testing;
using Xunit;

public sealed class OrderHandlerTests
{
    [Fact]
    public async Task HandleCreateOrder_Should_Store_OrderCreatedEvent_In_Outbox()
    {
        // 1. Arrange: In-Memory Outbox harness
        var store = new InMemoryOutboxStore();
        var outbox = store.CreateOutbox();
        var handler = new OrderCommandHandler(outbox);

        // 2. Act
        var orderId = Guid.NewGuid();
        await handler.CreateOrderAsync(orderId, "customer-123", 99.95m);

        // 3. Assert
        store.StoredMessages.Should().HaveCount(1);
        var stored = store.StoredMessages[0];
        stored.MessageType.Should().Be("order.created.v1");
        stored.CorrelationId.Should().NotBeNull();
    }
}
```

---

## Recipe 11: Exception-Aware Custom `IRetryPolicy`

### Problem
The built-in retry policies (`FixedDelayRetryPolicy`, `ExponentialBackoffRetryPolicy`, `JitterRetryPolicy`) apply the same strategy regardless of the exception type. You need to retry on transient exceptions (network timeout, connection reset) but **not** retry on permanent failures (schema mismatch, authorization denied).

### Solution
Implement `IRetryPolicy` directly, overriding `ShouldRetry(int, Exception)` to inspect the exception before deciding whether to retry.

### Code
```csharp
using EricksonLopez.Outbox.Retry;

// Exception-aware retry: only retries on specific transient exception types.
public sealed class ExceptionAwareRetryPolicy : IRetryPolicy
{
    private readonly int _maxAttempts;

    public ExceptionAwareRetryPolicy(int maxAttempts = 5) => _maxAttempts = maxAttempts;

    // ShouldRetry is called FIRST. Return false to stop immediately without computing delay.
    public bool ShouldRetry(int currentAttempt, Exception exception)
    {
        if (currentAttempt >= _maxAttempts) return false;

        // Retry only on recoverable failures
        return exception is TimeoutException
            || exception is System.Net.Sockets.SocketException
            || exception.Message.Contains("connection refused", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("503 Service Unavailable", StringComparison.OrdinalIgnoreCase);
    }

    // GetNextDelay: returns the wait before the next attempt.
    public TimeSpan GetNextDelay(int currentAttempt)
        => currentAttempt switch
        {
            1 => TimeSpan.FromSeconds(1),
            2 => TimeSpan.FromSeconds(5),
            3 => TimeSpan.FromSeconds(15),
            _ => TimeSpan.FromSeconds(30),
        };
}

// Registration in Program.cs:
builder.Services.AddOutbox(options =>
{
    options.UseBroker<RabbitMqPublisher>(
        retryPolicy: new ExceptionAwareRetryPolicy(maxAttempts: 4));
});
```

### Explanation
- `ShouldRetry` is evaluated before `GetNextDelay`. Returning `false` from `ShouldRetry` results in an immediate `DispatchResult.FailFatal`, moving the message to the DLQ without waiting for a delay.
- `GetNextDelay` is only called when `ShouldRetry` returned `true`.
- Contrast with `RetryPolicy` (abstract record): it only exposes `GetNextDelay` — it has no mechanism to inspect exception types. Use `IRetryPolicy` when exception-type discrimination is required.

### Best Practices
- Enumerate explicit recoverable exceptions rather than catching `Exception` broadly.
- Log the exception type in your broker publisher before returning `DispatchResult.FailAndRetry` to aid debugging.
- Set `maxAttempts` conservatively (3–5). More attempts increase the latency window during which the message is stuck.

### Common Errors
- **Returning `false` from `ShouldRetry` for `TimeoutException`**: Timeouts are typically transient. Returning `false` dead-letters the message on the first network glitch.
- **Returning `true` from `ShouldRetry` for authorization exceptions**: Auth errors are permanent — retrying them burns retry budget and delays dead-lettering.

---

## Recipe 12: Dead Letter Queue Administration (Purge + Crash Recovery)

### Problem
Your DLQ accumulates old entries. You also need a way to recover messages stuck in `InFlight` state after a dispatcher crash, without waiting for the automatic reclaim cycle.

### Solution
Use `IDeadLetterRepository.PurgeAsync()` for bulk DLQ cleanup, and `IOutboxRepository.ReclaimStaleMessagesAsync()` for crash recovery.

### Code
```csharp
using EricksonLopez.Outbox.Persistence;

// ─────────────────────────────────────────────────
// DLQ Retention Cleanup (run on a scheduled task / endpoint)
// ─────────────────────────────────────────────────

public static class DlqMaintenanceService
{
    public static async Task PurgeOldDeadLettersAsync(
        IDeadLetterRepository dlqRepository,
        int retentionDays = 30,
        CancellationToken ct = default)
    {
        // Bulk-deletes ALL DLQ entries older than the cutoff — no batching (DLQ is typically small).
        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);
        await dlqRepository.PurgeAsync(cutoff, ct);
    }
}

// ─────────────────────────────────────────────────
// Crash Recovery (manual reclaim — normally automatic)
// ─────────────────────────────────────────────────

public static class OutboxRecoveryService
{
    public static async Task<int> ReclaimStuckMessagesAsync(
        IOutboxRepository outboxRepository,
        TimeSpan? staleTimeout = null,
        CancellationToken ct = default)
    {
        // Resets InFlight messages older than staleTimeout back to Pending.
        // Default matches OutboxDispatcherOptions.ReclaimTimeout (5 minutes).
        var timeout = staleTimeout ?? TimeSpan.FromMinutes(5);
        return await outboxRepository.ReclaimStaleMessagesAsync(timeout, ct);
    }
}

// ─────────────────────────────────────────────────
// Minimal API endpoints for administrative use
// ─────────────────────────────────────────────────

app.MapDelete("/admin/dlq/purge", async (
    IDeadLetterRepository dlq,
    [FromQuery] int olderThanDays,
    CancellationToken ct) =>
{
    await dlq.PurgeAsync(DateTimeOffset.UtcNow.AddDays(-olderThanDays), ct);
    return Results.NoContent();
});

app.MapPost("/admin/outbox/reclaim", async (
    IOutboxRepository repo,
    CancellationToken ct) =>
{
    var reclaimed = await repo.ReclaimStaleMessagesAsync(TimeSpan.FromMinutes(5), ct);
    return Results.Ok(new { reclaimed });
});
```

### Explanation
- `PurgeAsync` is a single bulk DELETE — appropriate because DLQ tables are small (only messages that exhausted all retries). For high-volume DLQs, implement a custom `IDeadLetterRepository` with batched purge.
- `ReclaimStaleMessagesAsync` is idempotent and safe to call at any time. The dispatcher already calls it every `ReclaimInterval` (default: 1 min). Manual calls are useful in startup health checks or administrative tools after a known crash.

### Best Practices
- Purge the DLQ on a schedule (daily cron) rather than on every request.
- Set `staleTimeout` **longer** than your maximum dispatch attempt duration (including all retry delays within a single poll cycle). Too short causes false-positive reclaims and potential duplicates.
- Always log the count returned by `ReclaimStaleMessagesAsync` — a non-zero count signals a dispatcher crash or slow dispatch scenario.

### Common Errors
- **Calling `PurgeAsync` with `DateTimeOffset.MinValue`**: Purges ALL DLQ messages regardless of age. Always use a concrete cutoff date.
- **Setting `staleTimeout < PollingInterval + max broker timeout`**: Causes messages to be reclaimed before the dispatcher has time to dispatch them, leading to at-most-once delivery.

---

## Recipe 13: Implementing a Custom `IBrokerPublisher` with Correct `DispatchResult` Mapping

### Problem
You are writing a custom `IBrokerPublisher` (e.g., for a proprietary internal bus) and need to correctly map every possible broker outcome to the right `DispatchResult` factory method, including the rate-limit case (retry without incrementing the retry counter).

### Solution
Use all six `DispatchResult` factory method overloads as appropriate.

### Code
```csharp
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Persistence;

public sealed class InternalBusPublisher : IBrokerPublisher
{
    public string BrokerSystemName => "internal-bus";

    private readonly InternalBusClient _client;
    private readonly CircuitBreakerState _circuitBreaker;

    public InternalBusPublisher(InternalBusClient client)
    {
        _client = client;
        _circuitBreaker = new CircuitBreakerState(failureThreshold: 5, openDuration: TimeSpan.FromSeconds(30));
    }

    public async ValueTask<DispatchResult> PublishRawAsync(
        OutboxMessage message,
        OutboxMessageMetadata metadata,
        DispatchContext context)
    {
        // Circuit breaker: if Open, reject without hitting the bus.
        if (!_circuitBreaker.AllowRequest())
            return DispatchResult.FailAndRetry(
                new Exception("Internal bus circuit breaker is Open"),
                incrementRetryCount: false);  // Rate-limit: don't burn retry budget

        try
        {
            var destination = metadata.GetValue("x-destination") ?? message.MessageType;
            await _client.PublishAsync(destination, message.Payload, context.CancellationToken);

            _circuitBreaker.RecordSuccess();
            return DispatchResult.Ok();  // ✅
        }
        catch (BusRateLimitException ex)
        {
            // 429: temporary rate limit. Retry without incrementing RetryCount.
            _circuitBreaker.RecordFailure();
            return DispatchResult.FailAndRetry(ex, incrementRetryCount: false);
        }
        catch (BusTransientException ex)
        {
            // Transient: network glitch, broker restart. Retry AND increment RetryCount.
            _circuitBreaker.RecordFailure();
            return DispatchResult.FailAndRetry(ex);
        }
        catch (BusSchemaException ex)
        {
            // Permanent: schema incompatibility. Dead-letter immediately.
            return DispatchResult.FailFatal(ex);
        }
        catch (BusAuthorizationException)
        {
            // Permanent: authorization denied. No point retrying. Dead-letter with string reason.
            return DispatchResult.FailFatal("Bus authorization denied. Check service account credentials.");
        }
        catch (Exception ex)
        {
            // Unexpected failure. Treat as transient (safe default).
            _circuitBreaker.RecordFailure();
            return DispatchResult.FailAndRetry(ex);
        }
    }
}
```

### Explanation
- `DispatchResult.Ok()` → message is removed from outbox (or marked Dispatched if `DeleteOnDispatch = false`).
- `DispatchResult.FailAndRetry(ex)` → message is rescheduled with exponential backoff. `RetryCount` increments. After `MaxRetryCount`, the message is dead-lettered.
- `DispatchResult.FailAndRetry(ex, incrementRetryCount: false)` → message is rescheduled **without** incrementing `RetryCount`. Use for rate-limit scenarios where the message is valid but the broker is temporarily unavailable.
- `DispatchResult.FailFatal(ex)` or `DispatchResult.FailFatal(string)` → message goes to DLQ immediately. No more retries.

### Best Practices
- **Never throw** from `PublishRawAsync`. Unhandled exceptions bypass `DispatchResult` entirely and crash the dispatcher worker.
- **Never return `default(DispatchResult)`** — it is an incoherent zero-value state that dead-letters the message silently.
- Use `ThrowIfInvalid()` in unit tests: `result.ThrowIfInvalid()` validates that the result is a valid state.
- Wrap all broker calls in a `try/catch` — even connection setup (`var channel = await connection.CreateChannelAsync()`).

### Common Errors
- **Throwing `NotImplementedException` from a stub publisher**: The dispatcher catches nothing — the exception propagates and crashes the worker.
- **Returning `DispatchResult.Ok()` when the broker returned an error**: The message is silently lost.
- **Using `FailFatal` for transient errors**: The message bypasses all retry logic and goes directly to the DLQ on the first failure.
