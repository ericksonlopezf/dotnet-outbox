<!-- Copyright © Erickson Lopez. MIT License. -->

# Architecture Decision Records — Index

This document is the authoritative **index** of the Architecture Decision Records (ADRs) for `EricksonLopez.Outbox`. Each ADR documents a significant technical decision, its context, alternatives considered, and tradeoffs accepted.

The full ADR documents live in [`docs/adr/`](adr/).

---

## ADR Status Definitions

| Status | Meaning |
|---|---|
| **Accepted / Approved** | Implemented and enforced in the codebase |
| **Superseded** | Replaced by a newer ADR |
| **Deprecated** | No longer relevant |

---

## ADR Registry

| ADR | Title | Status | Key Decision |
|---|---|---|---|
| [ADR-001](adr/001-monorepo-modular-structure.md) | Monorepo Modular Structure | **Superseded** by ADR-009 | Original 6-project consolidated model |
| [ADR-002](adr/002-zero-allocation-models.md) | Zero-Allocation Models | Approved | `readonly record struct` + `ValueTask` throughout |
| [ADR-003](adr/003-postgresql-skip-locked.md) | PostgreSQL `SKIP LOCKED` | Approved | Lock-free concurrent polling via `FOR UPDATE SKIP LOCKED` |
| [ADR-004](adr/004-roslyn-source-generators-aot.md) | Roslyn Source Generators for AOT | Approved | Compile-time type mapping — zero runtime Reflection |
| [ADR-005](adr/005-idempotency-optimistic-inbox.md) | Optimistic Inbox Idempotency | Approved | `INSERT ... ON CONFLICT DO NOTHING` for deduplication |
| [ADR-006](adr/006-bounded-channels-dispatcher.md) | Bounded Channels Dispatcher | Approved | `System.Threading.Channels` for backpressure-aware dispatch |
| [ADR-007](adr/007-outboxmessage-readonly-record-struct.md) | `OutboxMessage` as `readonly record struct` | Approved | Stack-based, immutable, value-equality for the core data type |
| [ADR-008](adr/008-ref-struct-builder.md) | `ref struct OutboxMessageBuilder` | Approved | Zero-allocation fluent builder guaranteed to stay on the stack |
| [ADR-009](adr/009-package-consolidation-strategy.md) | Package Consolidation Strategy | Approved | Per-provider packages (17 projects) instead of consolidated |
| [ADR-010](adr/010-remove-dapper-raw-adonet.md) | Remove Dapper, Adopt Raw ADO.NET | Approved | Zero-allocation storage via raw `DbCommand`/`DbDataReader` |
| [ADR-011](adr/011-source-generator-json-context.md) | Source Generator JSON Context Limitation | Approved | Consumer-declared `JsonSerializerContext` with Analyzer validation |
| [ADR-012](adr/012-outboxmessage-positional-ctor-breaking-change.md) | `OutboxMessage` Binary Compatibility Evolution Strategy | Approved | Never add positional ctor params in v1.x; use `Extensions` or `init`-only properties |
| [ADR-013](adr/013-stryker-mutation-exclusions.md) | Stryker Mutation Testing — Exclusion Rationale | Approved | Logging, guard clauses, and generated code excluded from mutation scope |
| [ADR-014](adr/014-stryker-exclude-integration-tests.md) | Exclude Integration Tests from Stryker Mutation Scope | Approved | Integration tests excluded from Stryker to prevent false positives and timeouts |
| [ADR-015](adr/015-limitation-source-generator-json.md) | STJ `JsonSerializerContext` Auto-Generation — Roslyn Single-Pass Limitation | Approved | Roslyn single-pass constraint prevents cross-generator STJ integration |
| [ADR-016](adr/016-outbox-is-not-event-bus.md) | Outbox Is Not An Event Bus | Approved | Outbox strictly handles atomic persistence & external dispatch, not in-process pub/sub |
| [ADR-017](adr/017-no-exactly-once-delivery.md) | Outbox Does Not Guarantee Exactly-Once Delivery | Approved | Delivery guarantee is At-Least-Once; consumers must be idempotent |
| [ADR-018](adr/018-outbox-does-not-own-domain-events.md) | Outbox Does Not Own Domain Events | Approved | Domain Events belong to domain aggregates; Outbox receives integration messages |
| [ADR-019](adr/019-no-saga-orchestration.md) | Outbox Does Not Implement Saga Orchestration | Approved | Sagas belong to dedicated workflow engines; excluded from Outbox scope |
| [ADR-020](adr/020-no-broker-dependency-in-core.md) | Core Outbox Does Not Depend On Specific Brokers | Approved | Core is broker-agnostic (`IBrokerPublisher`); adapters live in `.Brokers.*` packages |
| [ADR-021](adr/021-no-integration-event-in-outbox.md) | `IIntegrationEvent` Is Not An Outbox Contract | Approved | Removed `IIntegrationEvent` coupling; Outbox accepts any serializable payload |
| [ADR-022](adr/022-inbox-is-separate-concern.md) | Consumer Idempotency (Inbox) Is A Separate Concern | Approved | Inbox separated from Outbox producer concerns for independent modularity |
| [ADR-023](adr/023-serialization-pluggable-aot-first.md) | Outbox Serialization Is Pluggable, AOT-First | Approved | `IOutboxSerializer` abstraction with STJ source generation as default |
| [ADR-024](adr/024-max-degree-parallelism-resolve.md) | `MaxDegreeOfParallelism` Implementation | Approved | Multi-worker parallel channel draining fully implemented in BackgroundService |
| [ADR-025](adr/025-no-scheduler.md) | Outbox Does Not Become A Scheduler | Approved | `DeliverAt` provides delivery delay only, not general job scheduling |
| [ADR-026](adr/026-no-reflection-based-discovery.md) | Outbox Does Not Use Reflection-Based Discovery | Approved | Compile-time Roslyn source generation (`OutboxTypeMappingGenerator`) |
| [ADR-027](adr/027-database-provider-tier-policy.md) | Database Provider Tier Policy | Approved | Tier-1 (PostgreSQL reference with UNNEST/SKIP LOCKED) vs Tier-2 Community Support |
| [ADR-028](adr/028-throw-on-unregistered-type-safe-default.md) | Safe Default For `ThrowOnUnregisteredType` | Approved | Defaults to `true` to fail-fast against unstable fallback CLR type names |
| [ADR-029](adr/029-dead-letter-repository-transaction-boundary.md) | DeadLetterRepository Standalone Transaction Boundary | Approved | Optional transaction parameter with auto-commit fallback for background error loops |
| [ADR-030](adr/030-osherove-test-naming-and-ide1006-suppression.md) | Roy Osherove Test Naming Standard & IDE1006 Suppression | Approved | Universal `Method_Scenario_ExpectedResult` test naming convention across test suite |
| [ADR-031](adr/031-mongodb-storage-strategy.md) | MongoDB Transactional Document Storage Strategy | Approved | `EricksonLopez.Outbox.Storage.MongoDb` with `IClientSessionHandle` support |
| [ADR-032](adr/032-dashboard-strategy.md) | Built-In Web Dashboard UI Exclusion Strategy | Approved | Core remains headless and zero-allocation; dashboard telemetry exposed via OpenTelemetry/Grafana |
| [ADR-033](adr/033-aspire-integration-strategy.md) | .NET Aspire Integration and Service Defaults | Approved | `EricksonLopez.Outbox.Aspire` host component automating OTLP metrics, tracing, and health checks |
| [ADR-034](adr/034-azure-event-hubs-strategy.md) | Azure Event Hubs High-Throughput Streaming Publisher | Approved | `EricksonLopez.Outbox.Brokers.AzureEventHubs` with zero-reflection payload streaming |
| [ADR-035](adr/035-messagemetadata-persistence-boundary-vs-messaging.md) | `OutboxMessageMetadata` Persistence Boundary vs Messaging Record | Approved | Retain `OutboxMessageMetadata` as zero-allocation struct for raw storage independent of messaging transports |
| [ADR-036](adr/036-legacy-mediatr-adapter-non-aot-deprecation.md) | Legacy MediatR Adapter Deprecation Strategy | Approved | Explicit non-AOT marking and staged deprecation path toward `EricksonLopez.Outbox.Mediator` |
| [REJECT-005](adr/reject-005-inbox-outbox-merging.md) | Rejection: Merging Inbox and Outbox into Monolithic Package | Rejected | Retain clean segregation between publishing outbox and consuming inbox concerns |

---

## Key Design Principles (Summary)

The following principles are the common thread across all ADRs:

1. **Zero Reflection** — All type resolution happens at compile time via Source Generators and `FrozenDictionary`.
2. **Zero Allocation on Hot Paths** — `ref struct` builders, `readonly record struct` models, `ArrayPool<T>`, `ValueTask`, `[ThreadStatic]` buffer writers.
3. **Raw ADO.NET First** — Pure `DbCommand`/`DbDataReader` with `FOR UPDATE SKIP LOCKED` is the canonical storage path (Dapper removed per ADR-010).
4. **Delete-on-Dispatch** — Successfully dispatched messages are deleted immediately (default) to prevent MVCC table bloat.
5. **NativeAOT by Default** — Every design decision is evaluated against NativeAOT compatibility first.
6. **Modularity & Ecosystem Boundaries** — Clear separation between Outbox, Events, Mediator, and Inbox concerns.
