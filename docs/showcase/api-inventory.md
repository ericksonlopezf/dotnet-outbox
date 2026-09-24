# API Public Inventory — EricksonLopez.Outbox Ecosystem

> **Source of Truth:** This document is generated directly from source code.
> The public API is the single source of truth. The Showcase must never contain examples of non-existent APIs.
>
> **Last Synchronized:** 2026-09-15

---

## Table of Contents

- [EricksonLopez.Outbox.Abstractions](#ericksonelopezoutboxabstractions)
- [EricksonLopez.Outbox — Core](#ericksonelopezoutbox--core)
- [EricksonLopez.Outbox — Persistence](#ericksonelopezoutbox--persistence)
- [EricksonLopez.Outbox — Retry](#ericksonelopezoutbox--retry)
- [EricksonLopez.Outbox — Pipeline](#ericksonelopezoutbox--pipeline)
- [EricksonLopez.Outbox — Dispatcher](#ericksonelopezoutbox--dispatcher)
- [EricksonLopez.Outbox — Serialization](#ericksonelopezoutbox--serialization)
- [EricksonLopez.Outbox — Hosting](#ericksonelopezoutbox--hosting)
- [EricksonLopez.Outbox — MultiTenancy](#ericksonelopezoutbox--multitenancy)
- [EricksonLopez.Outbox — Testing](#ericksonelopezoutbox--testing)
- [EricksonLopez.Outbox — Diagnostics](#ericksonelopezoutbox--diagnostics)
- [EricksonLopez.Inbox.Abstractions](#ericksonelopezinboxabstractions)
- [EricksonLopez.Inbox](#ericksonelopezinbox)
- [EricksonLopez.Outbox.Inbox](#ericksonelopezoutboxinbox)

---

## EricksonLopez.Outbox.Abstractions

Namespace: `EricksonLopez.Outbox`

| Type | Category | Key Members | Showcase |
|------|----------|-------------|----------|
| `IOutbox` | Interface | `StoreAsync<T>(msg, tx, ct)` · `StoreAsync<T>(ReadOnlyMemory<T>, tx, ct)` · `StoreAsync<T>(msg, tx, metadata, deliverAt, ct)` · `Publish<T>(msg)` | ✅ Level 1a, 1b, 2a, 5a–5e |
| `IOutboxTransactionContext` | Interface | Marker — wraps an active `DbTransaction` | ✅ Level 1a |
| `DbTransactionContext` | Class (sealed) | `ctor(DbTransaction)` · `Transaction { get; }` | ✅ Level 1a, 4 |
| `OutboxTransactionContextExtensions` | Static class | `ToOutboxContext(this DbTransaction)` | ✅ Level 1b |
| `OutboxExtensions` | Static class | `StoreAsync<T>(IEnumerable<T>, tx, ct)` · `EnqueueAsync` overloads | ✅ Level 5a, 5e |
| `OutboxPublishExtensions` | Static class | `EnqueueAsync<T>(single)` · `EnqueueAsync<T>(batch)` · `EnqueueAsync<T>(metadata+deliverAt)` | ✅ Level 5e |
| `OutboxMessageBuilder<TMessage>` | Class (sealed) | `WithTransaction` · `WithCorrelationId` · `WithCausationId` · `WithHeader` · `WithTenantId` · `WithDelay` · `WithDeliverAt` · `StoreAsync` | ✅ Level 2a, 2b, 5d |
| `OutboxMessageMetadata` | Record (readonly) | `CorrelationId` · `CausationId` · `MessageType` · `Entries` · `GetValue(key)` | ✅ Level 5c |
| `MetadataEntry` | Record struct | `Key` · `Value` | ✅ Level 5c |
| `OutboxMessageStatus` | Enum | `Pending(0)` · `InFlight(1)` · `Dispatched(2)` · `Failed(3)` · `DeadLettered(4)` | ✅ Level 6d |
| `OutboxMessageAttribute` | Attribute | `[OutboxMessage("alias")]` — marks types for the source generator | ✅ Level 8b (doc) |

---

## EricksonLopez.Outbox — Core

Namespace: `EricksonLopez.Outbox`

| Type | Category | Key Members | Showcase |
|------|----------|-------------|----------|
| `OutboxMessage` | Record (sealed) | `Id` · `MessageType` · `Payload` · `CorrelationId` · `CausationId` · `Headers` · `CreatedAt` · `ProcessedAt` · `DeliverAt` · `Status` · `RetryCount` · `Error` · `TenantId` · `Extensions` | ✅ Level 11d · ✅ Level 11h (TenantId, Extensions) |
| `IBrokerPublisher` | Interface | `PublishRawAsync(message, metadata, context)` · `string BrokerSystemName { get; }` | ✅ Level 8e (doc) |
| `ITypedBrokerPublisher` | Interface | `: IBrokerPublisher` · `PublishAsync<T>(MessageEnvelope<T>, context)` · `PublishBatchAsync<T>(list, context)` | ✅ Level 8e |
| `IBrokerSelector` | Interface | `GetPublisher(string messageType) → IBrokerPublisher` | ✅ Level 8d (doc) |
| `DispatchResult` | Record struct (readonly) | `Ok()` · `FailAndRetry(ex)` · `FailAndRetry(ex, bool)` · `FailFatal(ex)` · `FailFatal(string)` · `FailFatal(Guid, int, string)` · `ThrowIfInvalid()` | ✅ Level 6a · ✅ Level 6f |
| `DispatchContext` | Record struct | `CancellationToken` · `Attempt` | ✅ Level 9b (doc) |
| `DeadLetterMessage` | Record struct (readonly) | `FromOutboxMessage(original, retryCount, reason, lastError?)` · all fields | ✅ Level 11g |
| `FailedMessage` | Record | `FromOutboxMessage(original, retryCount, error, delay)` | ✅ Level 12 |
| `MessageEnvelope<T>` | Record | `Payload` · `Metadata` | ✅ Level 8e |
| `Publisher` | Record struct (readonly) | `Create(name)` · `None` · `Id` · `Name` · `RegisteredAt` | ✅ Level 8h |
| `Lease` | Record | `ResourceId` · `ConsumerId` · `ExpiresAt` · `IsExpired(now)` | ✅ Level 12 |
| `OutboxDispatchException` | Exception | Created internally by `DispatchResult.FailFatal(Guid, int, string)` | ✅ Level 6f |
| `OutboxPayloadTooLargeException` | Exception | `ActualSize` · `MaxAllowedSize` | ✅ Level 6e |

---

## EricksonLopez.Outbox — Configuration

Namespace: `EricksonLopez.Outbox`

| Type | Category | Key Members | Showcase |
|------|----------|-------------|----------|
| `OutboxOptions` | Class | `Configure` · `ConfigureRuntimeOptions` · `UseBroker<T>()` · `UseBroker(instance)` · `UseBroker(factory)` · `UsePostgreSql` · `UseSqlServer` · `UseMySql` · `UseMariaDb` · `UseOracle` · `UseSqlite` · `UseMongoDb` · `UseTypeResolver` · `UseMessagePackSerializer` · `UseProtobufSerializer` · `Route(alias)` · `RouteGroup(aliases)` · `UsePostgreSqlNotifications()` · `UseDapr(pubsub)` | ✅ Level 2, 7, 8 |
| `OutboxRuntimeOptions` | Class | `InstanceId` · `SchemaName` · `TableName` · `MaxPayloadSizeInBytes` · `MaxHeaderSizeInBytes` · `ThrowOnUnregisteredType` · `MaxMessageAge` · `MaxBackoffSeconds` · `LargeTableThreshold` · `DeleteOnDispatch` · `MaxStoreRatePerSecond` · `ReclaimBatchLimit` · `IncludeMessageTypeTag` | ✅ Level 7b |
| `OutboxDispatcherOptions` | Class | `BatchSize` · `MaxDegreeOfParallelism` · `PollingInterval` · `UseAdaptivePolling` · `ChannelCapacity` · `MaxBatchesPerSecond` · `MaxRetryCount` · `ReclaimTimeout` · `ReclaimInterval` · `DbRetryMaxAttempts` · `DbRetryBaseDelayMs` · `HasOnlySingletonMiddlewares` | ✅ Level 7a |
| `OutboxInboxOptions` | Class | `RetentionPeriod` · `DuplicateDetectionWindow` · `CleanupInterval` | ✅ Level 10c |
| `OutboxCleanupOptions` | Class | `Enabled` · `RetentionPeriod` · `CleanupInterval` · `BatchSize` | ✅ Level 2c |
| `OutboxHealthCheckOptions` | Class | `WarningThreshold` | ✅ Level 12 |
| `BrokerRouteBuilder` | Class | `ToPublisher(instance)` · `ToPublisher(factory)` · `ToPublisher<T>()` | ✅ Level 8d |
| `BrokerRouteGroupBuilder` | Class | `ToPublisher(instance)` · `ToPublisher(factory)` · `ToPublisher<T>()` | ✅ Level 8g |

---

## EricksonLopez.Outbox — Persistence

Namespace: `EricksonLopez.Outbox.Persistence`

| Type | Category | Key Members | Showcase |
|------|----------|-------------|----------|
| `IOutboxRepository` | Interface | `InsertAsync` · `InsertBatchAsync` · `FetchPendingAsync` · `MarkAsDispatchedAsync` · `MarkAsFailedAsync` · `ReclaimStaleMessagesAsync` · `GetPendingCountAsync` · `GetMessageAsync(id)` · `GetMessageAsync(id, createdAtHint)` · `PurgeDispatchedMessagesAsync` | ✅ Level 11a · 11d · 11e · **11f** |
| `IDeadLetterRepository` | Interface | `InsertAsync` · `GetAsync` · `DeleteAsync` · `PurgeAsync` · `IsFirstPartyImplementation` | ✅ Level 11b · 11c · **11g** |
| `OutboxTransactionContext` | Record | Alias — see Abstractions | ✅ Level 1a |
| `OutboxGenericTransactionContextExtensions` | Static class | `ToOutboxContext<T>(this T tx)` | ✅ Level 1b |

---

## EricksonLopez.Outbox — Retry

Namespace: `EricksonLopez.Outbox.Retry`

| Type | Category | Key Members | Showcase |
|------|----------|-------------|----------|
| `IRetryPolicy` | Interface | `GetNextDelay(attempt)` · `ShouldRetry(attempt, exception)` | ✅ **Level 6g** |
| `RetryPolicy` | Abstract record | `Default` (static) · `GetNextDelay(attempt)` | ✅ Level 6b |
| `FixedDelayRetryPolicy` | Record | `Delay` · `MaxAttempts` · `GetNextDelay` | ✅ Level 6b |
| `ExponentialBackoffRetryPolicy` | Record | `InitialDelay` · `MaxAttempts` · `Factor` · `MaxDelay` | ✅ Level 6b |
| `JitterRetryPolicy` | Record | `InitialDelay` · `MaxAttempts` · `Factor` · `MaxDelay` · `JitterFactor` | ✅ Level 6b |
| `CircuitBreakerState` | Class (sealed) | `ctor(failureThreshold, openDuration)` · `State` · `AllowRequest()` · `RecordSuccess()` · `RecordFailure()` · `FailureThreshold` · `OpenDuration` | ✅ Level 6c |
| `CircuitState` | Enum | `Closed(0)` · `Open(1)` · `HalfOpen(2)` | ✅ Level 6c |
| `RetryDispatcherInterceptor` | Class | Wraps `IBrokerPublisher` with retry policy | ℹ️ Internal |

---

## EricksonLopez.Outbox — Pipeline

Namespace: `EricksonLopez.Outbox.Pipeline`

| Type | Category | Key Members | Showcase |
|------|----------|-------------|----------|
| `IOutboxMiddleware` | Interface | `InvokeAsync(message, metadata, next, ct) → ValueTask<DispatchResult>` | ✅ Level 8c |
| `OutboxPipelineDelegate` | Delegate | `(OutboxMessage, OutboxMessageMetadata, ct) → ValueTask<DispatchResult>` | ✅ Level 8c |
| `OutboxPipeline` | Class | `ctor(middlewares, terminal)` · `ExecuteAsync(message, metadata, ct)` | ✅ Level 8c |

---

## EricksonLopez.Outbox — Dispatcher

Namespace: `EricksonLopez.Outbox.Dispatcher`

| Type | Category | Key Members | Showcase |
|------|----------|-------------|----------|
| `IPollerWakeup` | Interface | `WakeUp()` — external signal to wake up the poller | ✅ Level 7c |

---

## EricksonLopez.Outbox — Serialization

Namespace: `EricksonLopez.Outbox.Serialization`

| Type | Category | Key Members | Showcase |
|------|----------|-------------|----------|
| `IOutboxSerializer` | Interface | `Serialize<T>(message)` · `Serialize<T>(message, IBufferWriter<byte>)` · `Deserialize<T>(ReadOnlySpan<byte>)` | ✅ Level 8a |
| `NativeAotJsonSerializer` | Class (sealed) | `ctor(JsonSerializerContext)` | ✅ Level 8a |
| `IOutboxMessageTypeResolver` | Interface | `Resolve(alias)` · `TryGetAlias(Type, out string?)` · `TryGetAlias<T>(out string?)` · `GetAlias(Type)` · `GetAlias<T>()` · `GetAllMappings()` | ✅ Level 8b |
| `InMemoryMessageTypeResolver` | Class | `ctor(IEnumerable<(string alias, Type)>)` | ✅ Level 8b |

---

## EricksonLopez.Outbox — Hosting

Namespace: `EricksonLopez.Outbox.Hosting` / `EricksonLopez.Outbox`

| Type | Category | Key Members | Showcase |
|------|----------|-------------|----------|
| `OutboxServiceCollectionExtensions` | Static class | `AddOutbox(services, configure)` · `AddOutboxDispatcher(services, configure)` | ✅ Level 1 (Program.cs) |
| `OutboxHealthCheckExtensions` | Static class | `AddOutbox(IHealthChecksBuilder, name, warningThreshold, tags)` · `AddOutboxCleanupService(services, configure)` | ✅ Level 2c |
| `ManualOutboxDispatcher` | Class (sealed) | `ctor(serviceProvider, publisher, typeResolver)` · `DispatchPendingAsync(repository, batchSize, ct)` | ✅ Level 9a |
| `OutboxCleanupService` | Class | Background service — invokes `PurgeDispatchedMessagesAsync` according to `OutboxCleanupOptions` | ✅ Level 2c (doc) |
| `OutboxDispatcherBackgroundService` | Class | Background service — poller + dispatcher | ✅ Level 7a (doc) |
| `OutboxHealthCheck` | Class | `CheckHealthAsync(context, ct)` | ✅ Level 12 |

---

## EricksonLopez.Outbox — MultiTenancy

Namespace: `EricksonLopez.Outbox.MultiTenancy`

| Type | Category | Key Members | Showcase |
|------|----------|-------------|----------|
| `ITenantBrokerRouter` | Interface | `ResolveDestination(tenantId?, baseDestination, messageType) → string` | ✅ Level 8i |
| `ITenantConnectionResolver` | Interface | `ResolveConnectionStringAsync(tenantId, ct) → ValueTask<string>` | ✅ Level 8i |

---

## EricksonLopez.Outbox — Diagnostics

Namespace: `EricksonLopez.Outbox.Diagnostics`

| Type | Category | Key Members | Showcase |
|------|----------|-------------|----------|
| `OutboxActivitySource` | Static class | `StartDispatchActivity(messageType, correlationId, tenantId?)` · `StartStoreActivity(messageType, messageId, correlationId)` · `OutboxSystemName` | ✅ Level 12 |
| `OutboxMetrics` | Class (sealed) | `RecordStoreDuration(seconds, messageType)` · `CreateChannelFillGauge(valueFunc, capacity)` | ✅ Level 12 |
| `OutboxLogMessages` | Static class | 30+ extension methods on `ILogger` for structured events | ✅ Level 12 |
| `IErrorSanitizer` | Interface | `Sanitize(Exception) → string` | ✅ Level 8f |
| `DefaultErrorSanitizer` | Class (sealed) | Default implementation — returns `ex.ToString()` | ✅ Level 8f |

---

## EricksonLopez.Outbox — Testing

Namespace: `EricksonLopez.Outbox.Testing`

| Type | Category | Key Members | Showcase |
|------|----------|-------------|----------|
| `InMemoryOutboxStore` | Class | `StoreAsync<T>` · `Publish<T>` · `GetPublishedMessages<T>()` · `TotalPublishedCount()` · `Reset()` | ✅ Level 9b |
| `IOutboxTester` | Interface | `ShouldHavePublished<T>() → IOutboxAssertion<T>` | ✅ Level 9b |
| `IOutboxAssertion<T>` | Interface | `WithCondition(predicate)` · `Once()` · `Times(n)` · `AtLeastOnce()` · `Never()` | ✅ Level 9b |
| `OutboxTesterImpl` | Class | Wraps `InMemoryOutboxStore` | ✅ Level 9b |
| `TestingOutboxExtensions` | Static class | `ShouldHavePublished<T>()` · `ShouldHavePublished<T>(predicate)` · `ShouldHavePublishedOnce<T>()` · `ShouldHavePublishedTimes<T>(n)` · `ShouldNotHavePublished<T>()` · `TotalPublishedCount()` | ✅ Level 9b |
| `FakeBrokerPublisher` | Class | `WithSuccess()` · `WithFailure(ex)` · `PublishRawAsync` · `PublishAsync<T>` · `PublishBatchAsync<T>` | ✅ Level 9b |
| `FakeOutboxRepository` | Class | In-memory `IOutboxRepository` | ✅ Level 9b |
| `FakeDeadLetterRepository` | Class | In-memory `IDeadLetterRepository` | ✅ Level 9b |
| `FakeIdempotencyRepository` | Class | In-memory `IIdempotencyRepository` | ✅ Level 9b |
| `FakeOutboxDispatcher` | Class | `DispatchAsync` · `ShouldHaveDispatched(n)` · `ShouldHaveDispatchedNothing()` · `Reset()` | ✅ Level 9b |
| `FakeInboxIdempotencyChecker` | Class | In-memory `IInboxIdempotencyChecker` | ✅ Level 9b |
| `InMemoryInboxStore` | Class | `TryRecordAsync` · `HasBeenProcessedAsync` · `PurgeExpiredEntriesAsync` | ✅ Level 12 |

---

## EricksonLopez.Inbox.Abstractions

Namespace: `EricksonLopez.Inbox`

| Type | Category | Key Members | Showcase |
|------|----------|-------------|----------|
| `IIdempotencyChecker` | Interface | `HasProcessedAsync(messageId, consumerName, ct)` · `ExecuteIdempotentlyAsync(messageId, consumerName, handler, ct)` | ✅ Level 12 |
| `IInboxConsumerFilter` | Interface | `ExecuteIdempotentlyAsync(messageId, consumerName, handler, ct)` | ✅ Level 12 |
| `IInboxStore` | Interface | `TryRecordAsync(entry, ct)` · `HasBeenProcessedAsync(messageId, consumerId, ct)` · `PurgeExpiredEntriesAsync(before, ct)` | ℹ️ Extension point |
| `InboxEntry` | Record | `MessageId` · `ConsumerId` · `ProcessedAt` | ✅ Level 12 |

---

## EricksonLopez.Inbox

Namespace: `EricksonLopez.Inbox`

| Type | Category | Key Members | Showcase |
|------|----------|-------------|----------|
| `IInboxIdempotencyChecker` | Interface | `ShouldProcessAsync(messageId, consumerId, transaction, ct)` · `ShouldSkipAsync(messageId, transaction, ct)` | ✅ Level 10a, 10b |
| `IdempotencyChecker` | Class | Implements `IIdempotencyChecker` | ✅ Level 12 |
| `IdempotencyRecord` | Record | `MessageId` · `ConsumerName` · `ProcessedAt` | ✅ Level 12 |
| `IdempotentEventHandler<TEvent>` | Class | Wraps `IEventHandler<T>` with idempotency filter | ✅ Level 12 |
| `DefaultInboxConsumerFilter` | Class | Implements `IInboxConsumerFilter` via `IInboxStore` | ✅ Level 12 |
| `InboxCleanupService` | Class | Background service — purges expired records | ✅ Level 10c (doc) |
| `InboxExtensions` | Static class | `AddInbox(services, configure)` · `AddInMemoryInbox(services, configure)` | ✅ Level 12 |

---

## EricksonLopez.Outbox.Inbox

Namespace: `EricksonLopez.Outbox.Inbox`

| Type | Category | Key Members | Showcase |
|------|----------|-------------|----------|
| `InboxConsumerFilter` | Class | Implements `IInboxConsumerFilter` via `IInboxIdempotencyChecker` | ✅ Level 12 |
| `InboxConsumerRegistrationExtensions` | Static class | `AddInboxDeduplication(services)` | ✅ Level 12 |

---

## Coverage Summary

| Status | Meaning |
|--------|---------|
| ✅ | Covered with functional endpoint in Showcase |
| ℹ️ | Internal type or extension point — documented in Level 12 or by reference |

### Gaps Resolved in This Session

| Gap | Endpoint Created |
|-----|------------------|
| `DispatchResult.FailFatal(Guid, int, string)` — missing overload | **Level 6f** |
| `IRetryPolicy` — extensible interface for custom policies | **Level 6g** |
| `IOutboxRepository.ReclaimStaleMessagesAsync` — lacked dedicated endpoint | **Level 11f** |
| `IDeadLetterRepository.PurgeAsync` — missing from Level 11 | **Level 11g** |
| `IDeadLetterRepository.IsFirstPartyImplementation` — undocumented DIM property | **Level 11g** |
| `DeadLetterMessage.FromOutboxMessage` — undocumented factory method | **Level 11g** |
| `OutboxMessage.TenantId` / `.Extensions` — unexplored optional fields | **Level 11h** |
