<!-- Copyright © Erickson Lopez. MIT License. -->

# Product Roadmap & Delivery Status — EricksonLopez.Outbox

This document outlines the delivered features, architectural foundations, release milestones, and explicit architectural boundaries for the `EricksonLopez.Outbox` ecosystem.

---

## 🟢 Delivered Ecosystem Features (v1.x)

### 1. Ecosystem Adapters & Framework Integrations
Enterprise adapter packages providing seamless, AOT-compatible integration with leading .NET messaging frameworks:
- **`EricksonLopez.Outbox.Mediator`** — ✅ **Shipped**. NativeAOT source-generated mediator adapter (`EricksonLopez.Mediator`).
- **`EricksonLopez.Outbox.MassTransit`** — ✅ **Shipped**. Includes `MassTransitBrokerPublisher` adapter and `InboxIdempotencyFilter`.
- **`EricksonLopez.Outbox.MediatR`** — ✅ **Shipped**. Provides `OutboxNotificationPublisher` and `AddOutboxMediatRPublisher()` for transparent outbox persistence of `INotification` domain and integration events (legacy, ADR-036).
- **`EricksonLopez.Outbox.NServiceBus`** — ✅ **Shipped**. Provides `OutboxPublishBehavior`, `NServiceBusOutboxFeature`, and `EnableTransactionalOutbox()` extension for NServiceBus message pipelines.
- **`EricksonLopez.Outbox.Rebus`** — ✅ **Shipped**. Outgoing pipeline step `OutboxOutgoingStep` and `EnableTransactionalOutbox()` decorator for Rebus message pipelines.
- **`EricksonLopez.Outbox.Brighter`** — ✅ **Shipped**. Producer adapter `OutboxMessageProducer` and `AddOutboxBrighterProducer()` for Paramore.Brighter command processors.
- **`EricksonLopez.Outbox.Dapr`** — ✅ **Shipped**. Cloud-Native broker adapter `DaprBrokerPublisher` and `UseDapr()` / `AddDaprBrokerPublisher()` for Dapr Pub/Sub building blocks.

### 2. Standalone Consumer Deduplication & Events Packages
- **`EricksonLopez.Inbox`** & **`EricksonLopez.Inbox.Abstractions`** — ✅ **Shipped**. Standalone consumer idempotency and message deduplication engine (`IInboxStore`, `IInboxConsumerFilter`, `AddInboxDeduplication()`) backed by `IIdempotencyRepository` (ADR-022).
- **`EricksonLopez.Outbox.Events`** & **`EricksonLopez.Outbox.Inbox.Events`** — ✅ **Shipped**. First-class domain event and integration event transactional dispatch and idempotent consumption pipeline (`OutboxEventPublisher`, `IdempotentEventHandler<TEvent>`).
- **`EricksonLopez.Outbox.Inbox.AspNetCore`** — ✅ **Shipped**. ASP.NET Core endpoint filter automating `Idempotency-Key` HTTP header handling and deduplication.

### 3. High-Performance Binary Serializers
Pluggable binary serializers implementing `IOutboxSerializer`:
- **`EricksonLopez.Outbox.Serialization.Protobuf`** — ✅ **Shipped**. High-throughput binary serializer using `protobuf-net` with zero-allocation buffer writer support.
- **`EricksonLopez.Outbox.Serialization.MessagePack`** — ✅ **Shipped**. Ultra-fast binary serializer using `MessagePack-CSharp` with optional LZ4 compression.

### 4. Background Purging & Retention Engine
- **`OutboxCleanupService`** — ✅ **Shipped**. Native background service for periodic purging of soft-deleted (`DeleteOnDispatch = false`) dispatched records with configurable `RetentionPeriod`, `CleanupInterval`, and `BatchSize`.
- **`PurgeDispatchedMessagesAsync`** — ✅ **Shipped**. Default interface method on `IOutboxRepository` implemented across all storage engines (PostgreSQL, SQL Server, MySQL, MariaDB, Oracle, SQLite, MongoDB, EF Core, InMemory).

### 5. Cloud-Native & Streaming Storage/Brokers
- **`EricksonLopez.Outbox.Storage.MongoDb`** — ✅ **Shipped**. Transactional document storage for MongoDB with `IClientSessionHandle` support, atomic state transitions (`FindOneAndUpdate`), and NativeAOT-safe BSON mapping (ADR-031).
- **`EricksonLopez.Outbox.Brokers.AzureEventHubs`** — ✅ **Shipped**. High-throughput event streaming publisher for Azure Event Hubs using `EventHubProducerClient` with zero-reflection payload transmission (ADR-034).
- **`EricksonLopez.Outbox.Aspire`** — ✅ **Shipped**. .NET Aspire host component integration automatically registering OpenTelemetry meters, tracers, and health checks (ADR-033).

### 6. Compile-Time Tooling & Roslyn Analyzers
- **`EricksonLopez.Outbox.Analyzers`** — ✅ **Shipped**. 12 Roslyn Analyzers (OUTBOX001-OUTBOX013) with automated Code Fix Providers in the IDE to enforce correct outbox usage at compile time.
- **`EricksonLopez.Outbox.SourceGenerators`** — ✅ **Shipped**. Incremental source generator for compile-time type resolution and NativeAOT JSON serialization context templates.

---

## 🛑 Forward-Compatible Primitives & v2.0 Roadmap

The following foundational primitives have been introduced in v1.x in a non-breaking manner, paving the way for complete architectural evolution in v2.0:

### 1. Strongly-Typed Transaction Context (`IOutboxTransactionContext<TConnection, TTransaction>`)
- Introduced `IOutboxTransactionContext<TConnection, TTransaction>` non-breaking generic interface alongside `IOutboxTransactionContext` and `IRelationalOutboxTransactionContext` to support NoSQL transaction contexts (Marten, Cosmos DB, MongoDB).

### 2. Mockable Publishing Extensions (`OutboxPublishExtensions`)
- Added `outbox.EnqueueAsync<T>()` static extension methods delegating cleanly to `IOutbox.StoreAsync()`, ensuring 100% mockable producer calls in consumer code.

---

## ⛔ Formally Discarded / Out of Scope (Non-Goals & Ecosystem Boundaries)

In accordance with architectural ADRs, the following capabilities are **explicitly excluded** from the core Outbox scope and are either resolved by dedicated frameworks within the `EricksonLopez.*` ecosystem or delegated to external infrastructure:

### 1. Capabilities Resolved Within the `EricksonLopez.*` Ecosystem

1. **Saga / Process Manager Orchestration (ADR-019)**: Sagas are multi-step state machines. Outbox is an atomic publish primitive. Distributed saga orchestration, compensations, and process state machines are resolved by **`EricksonLopez.Processes`** (using **`EricksonLopez.Processes.Outbox`** as the side-effect publishing bridge).
2. **In-Process Event Bus / Mediator (ADR-016)**: Outbox does not dispatch events to in-process handlers. In-process pipeline execution and message dispatch belong to **`EricksonLopez.Mediator`**.
3. **Domain Event Ownership (ADR-018)**: Outbox never owns domain events. Domain models and aggregates defined in **`EricksonLopez.SharedKernel`** own domain events, which are mapped to integration messages at the application boundary.
4. **Exactly-Once Delivery (ADR-017)**: Exactly-once delivery across heterogeneous DB + Broker networks is impossible without distributed 2PC/XA. The outbox guarantees **At-Least-Once** delivery, while effective end-to-end deduplication and idempotent processing are resolved on the consumer side by **`EricksonLopez.Idempotency`** / **`EricksonLopez.Inbox`**.

### 2. Capabilities Delegated to External Tools & Infrastructure

5. **Generic Job Scheduling (ADR-025)**: `DeliverAt` provides delivery delay for messages; it is not a recurring cron/job scheduler (delegated to Quartz.NET or Hangfire).
6. **Built-in CDC / Transaction Log Tailing in Core**: WAL/CDC tailing is infrastructure-level transport, not part of the transactional outbox core library (delegated to Debezium or database-native logical replication).
7. **Admin UI Dashboard in Core**: Outbox core remains minimal, headless, and zero-allocation. Any dashboard tooling will be developed as external observability tooling.
