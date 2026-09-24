# Functional Map — EricksonLopez.Outbox Pipeline

> **Purpose:** This document describes the end-to-end execution flow of the Outbox system,
> from message storage to broker publication, including
> all error and recovery paths.
>
> **Relationship:** Complements the [API Inventory](./api-inventory.md) with a behavioral perspective.

---

## 1. Storage Flow (Producer Side)

```
Business Code
│
├─ IOutbox.Publish<TMessage>(msg)
│   └── OutboxMessageBuilder<TMessage>   ← Fluent API (Level 2)
│       ├─ .WithTransaction(tx)          ← IOutboxTransactionContext
│       ├─ .WithCorrelationId(id)
│       ├─ .WithCausationId(id)
│       ├─ .WithHeader(key, value)
│       ├─ .WithTenantId(tenantId)       ← ITenantBrokerRouter (Level 8i)
│       ├─ .WithDelay(TimeSpan)          ← Scheduled delivery (Level 5d)
│       ├─ .WithDeliverAt(DateTimeOffset)
│       └─ .StoreAsync(ct)
│           │
│           ▼
│   IOutboxSerializer.Serialize<T>(msg) → byte[]   ← JSON/MsgPack/Protobuf (Level 8a)
│   IOutboxMessageTypeResolver.GetAlias<T>()        ← Type registry (Level 8b)
│   OutboxRuntimeOptions validation
│       ├─ MaxPayloadSizeInBytes exceeded? → OutboxPayloadTooLargeException (Level 6e)
│       ├─ ThrowOnUnregisteredType? → exception if alias not found
│       └─ deliverAt > MaxMessageAge?  → ArgumentOutOfRangeException (Level 5d)
│           │
│           ▼
│   IOutboxRepository.InsertAsync(OutboxMessage, tx, ct)    ← Level 11 / PostgreSQL/SQL Server
│       WITHIN the same DbTransaction as the business operation
│       ↓
│       DB: INSERT INTO outbox.messages (id, message_type, payload, status=0, ...)
│       ↓
│       COMMIT (atomically with business INSERT/UPDATE)
│
├─ IOutbox.StoreAsync<T>(msg, tx, ct)                        ← Direct overload (Level 1a)
├─ IOutbox.StoreAsync<T>(ReadOnlyMemory<T>, tx, ct)          ← Batch zero-alloc (Level 5b)
├─ IOutbox.StoreAsync<T>(IEnumerable<T>, tx, ct)             ← Batch LINQ-friendly (Level 5a)
└─ IOutbox.StoreAsync<T>(msg, tx, metadata, deliverAt, ct)   ← Full control (Level 5c)
```

---

## 2. Dispatch Flow (Dispatcher Side)

```
OutboxDispatcherBackgroundService  (IHostedService)
│
├─ POLLER LOOP (per PollingInterval / AdaptivePolling)
│   │
│   ├─ [Signal] IPollerWakeup.WakeUp()    ← External notification, e.g. LISTEN/NOTIFY (Level 7c)
│   │
│   ├─ IOutboxRepository.FetchPendingAsync(batchSize, ct)
│   │   SQL: SELECT ... FOR UPDATE SKIP LOCKED WHERE status=0 AND deliver_at <= NOW()
│   │   SQL: UPDATE status=1 (InFlight) atomically
│   │   Returns: List<OutboxMessage>
│   │
│   └─ [For each message batch]
│       │
│       ▼
│   Channel<OutboxMessage>  (bounded, capacity=ChannelCapacity)
│       ↓ BackPressure: Writer waits if channel is full
│       ↓
│   [MaxDegreeOfParallelism consumers]
│       │
│       ▼
│   PIPELINE EXECUTION (per message)
│   │
│   OutboxPipeline.ExecuteAsync(message, metadata, ct)     ← Level 8c
│       │
│       ├─ Middleware 1 (IOutboxMiddleware.InvokeAsync → next)
│       ├─ Middleware 2 (IOutboxMiddleware.InvokeAsync → next)
│       └─ Terminal: IBrokerPublisher.PublishRawAsync(OutboxMessage, metadata, context)
│               │
│               ├─ [if multiple brokers] IBrokerSelector.GetPublisher(messageType) (Level 8d)
│               └─ [with RetryPolicy] RetryDispatcherInterceptor (Level 6b)
│                   │
│                   └─ CircuitBreakerState.AllowRequest() ?  (Level 6c)
│                       ├─ Open → DispatchResult.FailAndRetry (no-increment)
│                       └─ Closed/HalfOpen → PublishRawAsync
│                           │
│                           ├─ Returns DispatchResult.Ok()
│                           │   └─ cb.RecordSuccess()
│                           │   └─ IOutboxRepository.MarkAsDispatchedAsync(id)
│                           │       ├─ DeleteOnDispatch=true  → DELETE row
│                           │       └─ DeleteOnDispatch=false → UPDATE status=2 (Dispatched)
│                           │
│                           ├─ Returns DispatchResult.FailAndRetry(ex)
│                           │   └─ cb.RecordFailure()
│                           │   └─ RetryPolicy.ShouldRetry(attempt, ex)?
│                           │       ├─ YES → wait GetNextDelay(attempt) → retry loop
│                           │       └─ NO  → IOutboxRepository.MarkAsFailedAsync(id, retryCount, error, nextDeliverAt)
│                           │               UPDATE status=3, deliver_at = NOW() + backoff
│                           │               → message re-fetched on next poll cycle
│                           │
│                           └─ Returns DispatchResult.FailFatal(...)
│                               └─ IDeadLetterRepository.InsertAsync(DeadLetterMessage.FromOutboxMessage(...))
│                               └─ IOutboxRepository.MarkAsFailedAsync(id, ..., isDeadLetter:true)
│                                   UPDATE status=4 (DeadLettered)
```

---

## 3. Recovery Flow (Crash Recovery)

```
OutboxDispatcherBackgroundService
│
└─ RECLAIM LOOP (every ReclaimInterval, default: 1 min)
    │
    └─ IOutboxRepository.ReclaimStaleMessagesAsync(staleTimeout, ct)    ← Level 11f
        SQL: UPDATE status=0 WHERE status=1 AND updated_at < (NOW() - staleTimeout)
        Returns: int (messages reclaimed)
        │
        └─ Reclaimed messages re-enter the POLLER LOOP as Pending (0)
```

---

## 4. Cleanup Flow (Retention)

```
OutboxCleanupService  (IHostedService, optional)    ← Level 2c
│
└─ CLEANUP LOOP (every CleanupOptions.CleanupInterval, default: 1h)
    │
    └─ [Only when DeleteOnDispatch = false]
        IOutboxRepository.PurgeDispatchedMessagesAsync(cutoff, batchSize, ct)    ← Level 11e
        SQL: DELETE FROM outbox.messages WHERE status=2 AND processed_at < cutoff
        Batched to avoid lock escalation (default batch: 1000 rows)
```

---

## 5. Inbox / Idempotency Flow (Consumer Side)

```
Message received from broker
│
└─ IInboxIdempotencyChecker.ShouldProcessAsync(messageId, consumerId, tx, ct)    ← Level 10a
    │
    SQL: INSERT INTO inbox.idempotency_records (message_id, consumer_id, recorded_at)
         ON CONFLICT DO NOTHING
    │
    ├─ Inserted (new) → returns true  → execute business handler → COMMIT
    │
    └─ Conflict (duplicate) → returns false → ROLLBACK → ignore message
        │
        [Alternative pre-check, no-insert]
        IInboxIdempotencyChecker.ShouldSkipAsync(messageId, tx, ct)    ← Level 10b
            SELECT 1 FROM inbox.idempotency_records WHERE message_id = ?
            → true = already processed (skip)
            → false = new (process)

[Background] InboxCleanupService
    └─ Purges expired idempotency records older than OutboxInboxOptions.RetentionPeriod
```

---

## 6. Testing Flow (Zero-Mocking Pattern)

```
Test
│
├─ InMemoryOutboxStore (registers as IOutbox + InMemoryOutboxStore)    ← Level 9b
│   └─ StoreAsync<T>(msg, tx, ct) → stores in ConcurrentDictionary<string, List<object>>
│
├─ FakeBrokerPublisher.WithSuccess() / .WithFailure(ex)
│   └─ PublishRawAsync → returns configured DispatchResult
│
└─ Assertions via TestingOutboxExtensions
    ├─ store.ShouldHavePublished<OrderCreatedEvent>()
    ├─ store.ShouldHavePublished<OrderCreatedEvent>(e => e.OrderId == expected)
    ├─ store.ShouldHavePublishedOnce<OrderCreatedEvent>()
    ├─ store.ShouldHavePublishedTimes<OrderCreatedEvent>(3)
    ├─ store.ShouldNotHavePublished<OrderCreatedEvent>()
    └─ store.TotalPublishedCount() → int
```

---

## 7. Extensibility Map

```
Extension Point                   Interface/Class              Showcase
──────────────────────────────────────────────────────────────────────
Custom serialization              IOutboxSerializer            Level 8a
Custom type registry              IOutboxMessageTypeResolver   Level 8b
Pipeline middleware               IOutboxMiddleware            Level 8c
Broker routing by type            OutboxOptions.Route()        Level 8d
Custom broker publisher           IBrokerPublisher             Level 8e
Error sanitization                IErrorSanitizer              Level 8f
Type group routing                OutboxOptions.RouteGroup()   Level 8g
Custom retry policy               IRetryPolicy                 Level 6g  ← NEW
Multi-tenancy: broker routing     ITenantBrokerRouter          Level 8i
Multi-tenancy: connection         ITenantConnectionResolver    Level 8i
Custom Dead Letter repository     IDeadLetterRepository        Level 11g ← NEW
Custom Inbox store                IInboxStore                  Level 12
```

---

## 8. Diagnostics Map

```
OpenTelemetry Tracing
    OutboxActivitySource.StartStoreActivity(messageType, messageId, correlationId)
        → Activity: "outbox.store" (span)
    OutboxActivitySource.StartDispatchActivity(messageType, correlationId, tenantId?)
        → Activity: "outbox.dispatch" (span)

OpenTelemetry Metrics
    OutboxMetrics.RecordStoreDuration(seconds, messageType)
        → Histogram: "outbox.store.duration"
    OutboxMetrics.CreateChannelFillGauge(valueFunc, capacity)
        → ObservableGauge: "outbox.channel.fill_ratio"

Structured Logging
    OutboxLogMessages (30+ extension methods)
        OutboxLogMessages.MessageStored(logger, messageType, messageId)
        OutboxLogMessages.MessageDispatched(logger, messageType, messageId, duration)
        OutboxLogMessages.MessageFailed(logger, messageType, messageId, attempt, error)
        OutboxLogMessages.MessageDeadLettered(logger, messageType, messageId, reason)
        OutboxLogMessages.PollerWakeUp(logger)
        OutboxLogMessages.StaleMessagesReclaimed(logger, count)
        ... and 20+ more events

Health Check
    IHealthChecksBuilder.AddOutbox(name, warningThreshold, tags)
        → OutboxHealthCheck.CheckHealthAsync()
        → Calls IOutboxRepository.GetPendingCountAsync()
        → Healthy if count < warningThreshold (default: no limit)
        → Degraded if count >= warningThreshold
```

---

## 9. Message State Map

```
Pending (0)
    │
    ├─ [deliver_at <= NOW()] FetchPendingAsync()
    │       ↓
    │   InFlight (1)
    │       │
    │       ├─ [PublishRawAsync → Ok()] MarkAsDispatchedAsync()
    │       │       ├─ DeleteOnDispatch=true  → [ROW DELETED]
    │       │       └─ DeleteOnDispatch=false → Dispatched (2)
    │       │
    │       ├─ [PublishRawAsync → FailAndRetry()] MarkAsFailedAsync()
    │       │       → Failed (3)
    │       │           ↑
    │       │           └─ [deliver_at <= NOW()] FetchPendingAsync() re-fetches → InFlight (1)
    │       │                                    (exponential backoff delays re-fetch)
    │       │
    │       ├─ [RetryCount >= MaxRetryCount] MarkAsFailedAsync(isDeadLetter:true)
    │       │       → DeadLettered (4)  +  IDeadLetterRepository.InsertAsync()
    │       │
    │       └─ [Dispatcher crash] ReclaimStaleMessagesAsync(staleTimeout)
    │               → Pending (0)  ← back to start
    │
    └─ [deliver_at > NOW()] stays Pending until scheduled time
```
