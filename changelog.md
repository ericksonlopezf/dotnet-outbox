<!-- Copyright © Erickson Lopez. MIT License. -->

# Changelog

All notable changes to `EricksonLopez.Outbox` will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

---

## [2.0.0] - 2026-08-29

### Breaking Changes

- **[BC-001] Deletion of `IIntegrationEvent` Interface (`EricksonLopez.Outbox.Contracts`)**:
  - **Previous State**: `IIntegrationEvent : IMessage` interface existed in `EricksonLopez.Outbox.Contracts.dll` as the base contract for integration events.
  - **New State**: `IIntegrationEvent` has been completely deleted. Message publication now operates on POCO types annotated with `[OutboxMessage]` or domain event abstractions in `EricksonLopez.Outbox.Events` / `EricksonLopez.Outbox.Inbox.Events`.
  - **Affected Consumers**: Applications implementing `IIntegrationEvent` or referencing the `EricksonLopez.Outbox.Contracts` package.
  - **Migration Guidance**: Remove `: IIntegrationEvent` inheritance from message records/classes. Decorate message models with `[OutboxMessage(Topic = "...")]` or reference `EricksonLopez.Outbox.Events` / `EricksonLopez.Outbox.Inbox.Events` for first-class event handling.

- **[BC-002] Removal of `Publish<TMessage>` Interface Method from `IOutbox`**:
  - **Previous State**: `IOutbox` contained the fluent builder method `IOutboxMessageBuilder Publish<TMessage>(TMessage message)`.
  - **New State**: `IOutbox` is now a pure, minimal persistence contract containing only `StoreAsync` overloads. The `outbox.Publish(message)` builder is now provided as a C# extension method in `EricksonLopez.Outbox.OutboxPublishExtensions`.
  - **Affected Consumers**: Mocking frameworks and unit tests mocking `IOutbox.Publish(...)` directly (e.g., `Substitute.For<IOutbox>()` or `new Mock<IOutbox>()`), custom `IOutbox` decorators, and uncompiled binary consumers.
  - **Migration Guidance**: In test doubles and mocks, configure and verify `IOutbox.StoreAsync(...)` instead of `Publish`. In production code, add `using EricksonLopez.Outbox;` so the extension method is in scope, and recompile.

- **[BC-003] Rename and Relocation of `MessageMetadata` to `OutboxMessageMetadata`**:
  - **Previous State**: `struct MessageMetadata` was defined in namespace `EricksonLopez.Outbox` inside `EricksonLopez.Outbox.dll`.
  - **New State**: Renamed to `readonly struct OutboxMessageMetadata` and moved to namespace `EricksonLopez.Outbox.Abstractions` inside `EricksonLopez.Outbox.Abstractions.dll`.
  - **Affected Consumers**: Any code explicitly referencing the `MessageMetadata` type or passing raw metadata struct instances.
  - **Migration Guidance**: Replace all occurrences of `MessageMetadata` with `OutboxMessageMetadata`, add `using EricksonLopez.Outbox.Abstractions;`, and recompile.

- **[BC-004] Assembly Segregation & Extraction of `EricksonLopez.Outbox.Abstractions.dll`**:
  - **Previous State**: Core abstractions (`IOutbox`, `OutboxMessageStatus`, `IdempotencyRecord`, `[OutboxMessage]`, `[IdempotentConsumer]`, `IIdempotencyRepository`, `IOutboxTransactionContext`, `IRelationalOutboxTransactionContext`, `DbTransactionContext`, `IOutboxSerializer`) resided in `EricksonLopez.Outbox.dll`.
  - **New State**: All foundational interfaces and attributes are extracted to a standalone, zero-dependency contract assembly `EricksonLopez.Outbox.Abstractions.dll` without binary `[TypeForwardedTo]` in `EricksonLopez.Outbox.dll`.
  - **Affected Consumers**: Pre-compiled assemblies compiled against v1.0.0 referencing these types in `EricksonLopez.Outbox.dll`.
  - **Migration Guidance**: Recompile downstream projects against v2.0.0 packages. In domain and application layers, replace references to `EricksonLopez.Outbox` with the lightweight `EricksonLopez.Outbox.Abstractions` package.

- **[BC-005] Signature Modification in `IBrokerPublisher.PublishRawAsync`**:
  - **Previous State**: `ValueTask<DispatchResult> PublishRawAsync(OutboxMessage message, MessageMetadata metadata, DispatchContext context)`.
  - **New State**: Parameter 2 type changed from `MessageMetadata` to `OutboxMessageMetadata`.
  - **Affected Consumers**: Custom implementations of `IBrokerPublisher`.
  - **Migration Guidance**: Update the signature of `PublishRawAsync` in custom broker publisher implementations to accept `OutboxMessageMetadata` and recompile.

- **[BC-006] Signature Modification in `IOutbox.StoreAsync`**:
  - **Previous State**: `Task<Guid> StoreAsync<TMessage>(TMessage message, IOutboxTransactionContext transactionContext, MessageMetadata metadata, DateTimeOffset? deliverAt = null, CancellationToken cancellationToken = default)`.
  - **New State**: Parameter 3 type changed from `MessageMetadata` to `OutboxMessageMetadata`.
  - **Affected Consumers**: Custom outbox repository decorators and explicit manual callers of `IOutbox.StoreAsync`.
  - **Migration Guidance**: Update method signatures in custom storage providers and callers to pass `OutboxMessageMetadata`.

- **[BC-007] Signature Modification in `IOutboxMiddleware.InvokeAsync` & `OutboxPipelineDelegate`**:
  - **Previous State**: Middleware delegates and `IOutboxMiddleware.InvokeAsync` accepted `MessageMetadata`.
  - **New State**: Parameter type changed to `OutboxMessageMetadata`.
  - **Affected Consumers**: Custom pipeline middleware implementations registered in the outbox middleware pipeline.
  - **Migration Guidance**: Update custom middleware implementations to accept `OutboxMessageMetadata` in `InvokeAsync` and recompile.

- **[BC-008] Property Type Modification in `MessageEnvelope<T>.Metadata`**:
  - **Previous State**: `public MessageMetadata Metadata { get; init; }`.
  - **New State**: `public OutboxMessageMetadata Metadata { get; init; }`.
  - **Affected Consumers**: Any consumer code reading or populating `MessageEnvelope<T>.Metadata`.
  - **Migration Guidance**: Update property type references to `OutboxMessageMetadata` and recompile.

- **[BC-009] Removal of Native AOT & Trimming Support in `EricksonLopez.Outbox.MassTransit`**:
  - **Previous State**: `EricksonLopez.Outbox.MassTransit` was configured with `<IsAotCompatible>true</IsAotCompatible>` and `<IsTrimmable>true</IsTrimmable>`.
  - **New State**: Native AOT and Trimming flags set to `false` due to upstream MassTransit reflection requirements.
  - **Affected Consumers**: Applications publishing Native AOT binaries while referencing `EricksonLopez.Outbox.MassTransit`.
  - **Migration Guidance**: For Native AOT runtime targets, migrate away from MassTransit to native transport packages (`EricksonLopez.Outbox.Brokers.RabbitMQ`, `Kafka`, `AzureServiceBus`, `AwsSqs`, `GooglePubSub`, `Nats`, `RedisStreams`) or `EricksonLopez.Outbox.Events`.

- **[BC-010] Introduction of Strict Runtime Payload and Scheduling Validation Exceptions**:
  - **Previous State**: Oversized message payloads, oversized headers, or messages scheduled past `MaxMessageAge` resulted in silent truncation, unhandled storage errors, or dispatcher poller starvation.
  - **New State**: `IOutbox.StoreAsync` strictly validates constraints and throws `OutboxPayloadTooLargeException`, `OutboxHeadersTooLargeException`, or `ArgumentOutOfRangeException` (when `deliverAt > MaxMessageAge`).
  - **Affected Consumers**: Systems storing exceptionally large messages or scheduling messages far into the future beyond `MaxMessageAge`.
  - **Migration Guidance**: Ensure stored payloads and headers do not exceed configured limits (or increase `MaxPayloadSizeBytes` in `OutboxOptions`), and ensure scheduled `deliverAt` timestamps are within the `MaxMessageAge` retention window.

- **[BC-011] Consolidation of PostgreSQL DDL Scripts into `Outbox_DDL.sql`**:
  - **Previous State**: PostgreSQL schema was distributed across four discrete scripts (`01_Init_Outbox.sql`, `02_Indexes.sql`, `03_Partitioning.sql`, `04_ReclaimIndex.sql`).
  - **New State**: Individual numbered scripts removed and consolidated into a unified idempotent deployment script `scripts/postgres/Outbox_DDL.sql`.
  - **Affected Consumers**: Automated database deployment pipelines (Flyway, Liquibase, DbUp, EF Core raw migration tasks) executing the legacy script paths.
  - **Migration Guidance**: Update database migration automation runners and CI/CD deployment jobs to point to `scripts/postgres/Outbox_DDL.sql`.

### Added
- **`EricksonLopez.Outbox.Storage.MongoDb`**: Native transactional document storage for MongoDB with `IClientSessionHandle` support, atomic state updates (`FindOneAndUpdate`), and NativeAOT safety (ADR-031).
- **`EricksonLopez.Outbox.Brokers.AzureEventHubs`**: High-throughput broker transport for Azure Event Hubs with zero-reflection payload streaming via `EventHubProducerClient` (ADR-034).
- **`EricksonLopez.Outbox.Aspire`**: Host application integration component for .NET Aspire automating OpenTelemetry metrics, tracing, and health checks (ADR-033).
- **`EricksonLopez.Outbox.Events` & `EricksonLopez.Outbox.Inbox.Events`**: First-class domain event and integration event transactional dispatch and idempotent consumption pipeline (`OutboxEventPublisher`, `IdempotentEventHandler<TEvent>`).
- **`EricksonLopez.Outbox.Inbox.AspNetCore`**: ASP.NET Core endpoint filter automating `Idempotency-Key` HTTP header handling and deduplication.
- **Stryker Mutation Matrix Quality Gate**: 34-package parallel Stryker mutation testing workflow (`mutation-testing.yml`) and automated commit status validation gate (`scripts/verify-mutation-gate.js`) enforced before NuGet packaging.
- **Roslyn Analyzers Suite (OUTBOX001-OUTBOX013)**: Complete analyzer set and automated Code Fix Providers in the IDE.
- **Documentation Guides**: Added comprehensive operational guides for Multi-Tenancy (`docs/multi-tenancy.md`), Error Sanitization (`docs/error-sanitization.md`), and Rate Limiting (`docs/rate-limiting.md`).
- **Architectural Decision Records**: Added ADR-031 (MongoDB), ADR-032 (Dashboard), ADR-033 (Aspire), ADR-034 (Azure Event Hubs), ADR-035 (MessageMetadata Persistence Boundary), and ADR-036 (Legacy MediatR Adapter Deprecation).

### Fixed
- **Showcase Compilation & Synchronization**: Resolved `Result<T>` namespace ambiguity (`CS0104`) and synchronized all 11 progressive showcase endpoints in `Sample.OrderService`.
- **Analyzer ID Duplication**: Resolved diagnostic collision between `OUTBOX006` (Missing OutboxMessage attribute) and `OUTBOX013` (Missing JsonSerializable attribute).
- **Comparative Analysis Docs**: Fixed resilience description to clarify the built-in `CircuitBreakerState` with zero external Polly dependency.
- **Dead Zone Scheduling Guard**: Documented `ArgumentOutOfRangeException` preventing silent message starvation when `deliverAt > MaxMessageAge`.
- **Payload & Header Overflow Guards**: Documented `OutboxPayloadTooLargeException` and `OutboxHeadersTooLargeException`.

## [1.0.0] - 2026-08-08

### Added

- Initial public release of `EricksonLopez.Outbox`.
- Core abstractions: `IOutbox`, `IOutboxTransactionContext`, `IBrokerPublisher`, `IOutboxRepository`,
  `IIdempotencyRepository`, `IDeadLetterRepository`.
- Dispatcher background service with Adaptive Polling, Exponential Backoff, and Dead Letter Queue.
- EF Core integration (`EricksonLopez.Outbox.EntityFrameworkCore`).
- Storage providers: PostgreSQL, SQL Server, MySQL, Oracle, SQLite (raw ADO.NET, no Dapper).
- Broker publishers: RabbitMQ, Kafka, Azure Service Bus, AWS SQS, Google Pub/Sub, NATS, Redis Streams.
- MassTransit integration (`EricksonLopez.Outbox.MassTransit`).
- Roslyn Source Generator (`EricksonLopez.Outbox.SourceGenerators`) for compile-time type mapping.
- Roslyn Analyzers (`EricksonLopez.Outbox.Analyzers`) with OUTBOX001 and OUTBOX002 diagnostics.
- NativeAOT compatibility: `IsAotCompatible=true` on all core and storage packages; validated by dedicated CI smoke test.
- Strong Name Signing, Sigstore Provenance Attestation, and NuGet Trusted Publishing (OIDC) in the publish pipeline.
- **EventId 10011 (`InvalidDispatchResultDetected`)**: New source-generated log event for the case when
  `IBrokerPublisher.PublishAsync` returns `default(DispatchResult)`. Previously this was an inline `LogWarning`
  with untracked EventId; it is now a fully catalogued, zero-allocation log message.
- **[AUDIT-ISSUE-4] `ExecuteDbWithRetryAsync` — exponential backoff with jitter**: The previous linear backoff
  (`baseDelayMs * attempt`) has been replaced with exponential backoff (`baseDelayMs * 2^(attempt-1)`) capped
  at 2^10 × base, plus ±25% random jitter via `Random.Shared`. This prevents synchronized storm recovery when
  N concurrent consumers all fail on the same transient DB blip and retry at identical linear intervals.
- **[AUDIT-ISSUE-16] `PendingCountRefreshInterval` — configurable metric refresh**: The `messaging.outbox.pending.messages`
  OpenTelemetry gauge was previously refreshed on a hardcoded 30-second interval. Added `OutboxDispatcherOptions.PendingCountRefreshInterval`
  (default: 30s) so high-throughput deployments with strict SLA monitoring can reduce the interval to 5–10 seconds
  to get near-real-time backlog visibility without changing source code.
- **[AUDIT-RIESGO-2] ADR-012 — `OutboxMessage` positional constructor evolution strategy**: Added
  `docs/adr/012-outboxmessage-positional-ctor-breaking-change.md` documenting the binary compatibility risk
  of adding positional constructor parameters and the approved evolution strategy: use `Extensions` dictionary
  or `init`-only properties for v1.x additions; positional ctor changes deferred to next major version.
- **Validator hardening**: `OutboxDispatcherOptionsValidator` now validates `PendingCountRefreshInterval`,
  `DbRetryMaxAttempts`, and `DbRetryBaseDelayMs` at startup, failing fast with descriptive error messages.

### Changed

- **[P2-ISSUE-CC3] `MaxDegreeOfParallelism` clarification** (`OutboxDispatcherOptions`): Property now documents that it
  is **not yet implemented** in v1.0 and is reserved for a future major release. Setting it has no effect in the
  current version.
- **[P2-ISSUE-PKG2] `xunit.runner.visualstudio` package visibility**: Added `PrivateAssets="all"` to prevent the
  test runner adapter from leaking as a transitive dependency into production assemblies that reference test projects.
- **[P2-ISSUE-BIN1] `PackageValidationBaselineVersion` activation guide** (`Directory.Build.props`): The commented-out
  property now includes a detailed step-by-step guide explaining when and how to enable binary compatibility validation
  after the first NuGet publish.
- **[P0] DLQ failure behavior** (`OutboxChannel.HandleFailureAsync`): `isDeadLetterFinal` is now always `true`.
  Previously, if the DLQ INSERT failed, the message was left in state=3 (Failed) to allow future retry — this was
  a correctness bug (infinite polling loop). Consumers relying on the old behavior must now implement a custom
  `IDeadLetterRepository` that handles its own retry/fallback logic.
- **[P1] Source Generator — `UseGeneratedTypes()` docs**: The no-arg overload now has accurate XML docs pointing
  users to `UseGeneratedTypes(JsonSerializerContext)` and the template in `obj/OutboxJsonContext.g.cs`.
  The `UseGeneratedTypes(JsonSerializerContext)` overload XML docs are improved with actionable guidance.
- **[P1] Zero-alloc log path** (`OutboxChannel`): The inline `_logger.LogWarning(...)` call for
  `default(DispatchResult)` detection was replaced with a source-generated `[LoggerMessage]` method
  (`OutboxLogMessages.InvalidDispatchResultDetected`), eliminating `params object[]` allocation on every
  call even when the Warning log level is disabled.
- **[P2] `OutboxMessageBuilder.StoreAsync` pool leak**: When `StoreAsync()` was called without first calling
  `WithTransaction()`, the `InvalidOperationException` was thrown without calling `Dispose()`. Any headers
  added via `WithHeader()` before the exception would leak the rented `ArrayPool<byte>` array. **Fix**: `Dispose()`
  is now called before throwing.

### Fixed

- **[P1-ISSUE-ERR1] Cancellation dead-letters messages during rolling deploys** (`RetryDispatcherInterceptor`): When the
  `CancellationToken` was signalled during the retry delay (e.g., rolling deployment SIGTERM, `Ctrl+C`), the interceptor
  returned `DispatchResult.FailFatal(new OperationCanceledException())`. This caused the dispatcher to move the message
  to state=4 (DeadLettered), permanently losing it. **Fix**: Returns `DispatchResult.FailAndRetry(ex, incrementRetryCount: false)`
  instead. The message is set to state=3 (Failed/Retry) with the retry count unchanged. On next startup,
  `ReclaimStaleMessagesAsync` restores it to state=0 (Pending) within `ReclaimTimeout` (default 5 minutes). A
  `LogWarning` is emitted explaining the shutdown signal and reclaim plan.
- **[P1-ISSUE-C1] `IInboxIdempotencyChecker.ShouldSkipAsync` hardcoded `"outbox-dispatcher"` consumerId**: The method
  hardcoded `"outbox-dispatcher"` as the consumerId for every idempotency check. If called from a user-facing consumer,
  its idempotency records would silently collide with the dispatcher's internal records. **Fix**: Added a `consumerId`
  parameter (default `OutboxConstants.DispatcherConsumerId`) to the interface, implementation, and fake test double.
  New `OutboxConstants` class exposes `DispatcherConsumerId = "outbox-dispatcher"` as a named constant with documentation
  explaining the collision risk. Fully backward-compatible: all existing call sites use the default.
- **[P2-ISSUE-SQL3] Hardcoded `LIMIT 1000` in reclaim SQL** (`PostgreSqlOutboxRepository.ReclaimStaleMessagesAsync`):
  In high-load environments with cascading crash scenarios, 1000 stale messages per reclaim cycle may be insufficient.
  **Fix**: Added `OutboxRuntimeOptions.ReclaimBatchLimit` (default `1000`) to make this configurable. The SQL now
  uses `LIMIT @ReclaimLimit` with the configured value bound at execution time.
- **[P2-ISSUE-SG3] No warning when no `[OutboxMessage]` types found** (`OutboxTypeMappingGenerator`): Adding the outbox
  NuGet package but forgetting to annotate message types with `[OutboxMessage]` produced no build-time error — the
  failure only appeared at runtime when first calling `IOutbox.StoreAsync<T>()`. **Fix**: New `OUTBOXSG003` diagnostic
  (Warning) fires when the assembly references `EricksonLopez.Outbox` but has zero `[OutboxMessage]`-annotated types.
- **[P3-ISSUE-PERF4] Double `Stopwatch.GetElapsedTime` call** (`OutboxChannel.ProcessMessage`): The dispatch duration
  was computed twice — once for the OpenTelemetry metric (`.TotalSeconds`) and once for the structured log
  (`.TotalMilliseconds`). **Fix**: Elapsed computed once as a `TimeSpan`; both values derived from it.
- **[P0] DLQ infinite loop** (`OutboxChannel.HandleFailureAsync`): When a Dead Letter Queue (DLQ) INSERT fails, the message was
  previously left in state=3 (Failed) with `retry_count >= MaxRetryCount`. Because state=3 is included in the poller's
  `WHERE state IN (0,3)` clause, the dispatcher re-fetched and attempted to dead-letter the message indefinitely.
  **Fix**: `isDeadLetterFinal` is now always `true` — the outbox row is always promoted to state=4 (DeadLettered)
  regardless of whether the DLQ INSERT succeeds. If the DLQ INSERT fails, the record is missing from the DLQ table
  but the poller will no longer reprocess the message. Operators are alerted via the `DlqInsertFailed` log (level=Error)
  which includes the message ID for manual recovery.
- **[P1] Source Generator — deterministic hash**: `OutboxTypeMappingGenerator` previously used `string.GetHashCode()`
  (non-deterministic: uses randomized seeds in .NET) to generate the `contextName` class name embedded in
  `OutboxJsonContext.g.cs`. This caused the class name to change between build sessions, invalidating the Roslyn
  incremental generator cache on every build. **Fix**: Replaced with a DJB2-style polynomial hash function
  (`GetDeterministicHash`) that produces stable output across all .NET builds.
- **[P1] Source Generator — improved JSON context template**: `OutboxJsonContext.g.cs` now emits a more accurate
  and actionable guide. The comments correctly document the Roslyn design constraint (Microsoft/roslyn#57239) that
  prevents the STJ source generator from processing output from another generator in the same pass. The template
  class name was standardized to `OutboxGeneratedJsonContext` for clarity.

[Unreleased]: https://github.com/ericksonlopezf/dotnet-outbox/compare/v2.0.0...HEAD
[2.0.0]: https://github.com/ericksonlopezf/dotnet-outbox/compare/v1.0.0...v2.0.0
[1.0.0]: https://github.com/ericksonlopezf/dotnet-outbox/releases/tag/v1.0.0
