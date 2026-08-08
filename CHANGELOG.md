# Changelog

All notable changes to `EricksonLopez.Outbox` will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

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

[1.0.0]: https://github.com/ericksonlopezf/dotnet-outbox/releases/tag/v1.0.0
