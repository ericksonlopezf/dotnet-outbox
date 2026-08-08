# Product Roadmap

This document outlines the strategic vision and upcoming features for the `EricksonLopez.Outbox` ecosystem. 

*Note: This roadmap is subject to change based on community feedback and contributions.*

## 🟡 Short-Term Goals

### 1. Ecosystem Integrations (The "Trojan Horse" Strategy)
To accelerate adoption and provide zero-friction migration paths for enterprise teams, we will build first-class integration packages for existing messaging frameworks:
- ~~**`EricksonLopez.Outbox.MassTransit`**~~ — ✅ **Shipped in v2.1.0.** Includes `MassTransitBrokerPublisher` adapter and `InboxIdempotencyFilter`.
- **`EricksonLopez.Outbox.MediatR`** (Highest Priority): Transparent out-of-process durability for `INotification` publishers.
- **`EricksonLopez.Outbox.NServiceBus`**: A high-performance, AOT-friendly Outbox alternative for enterprise NServiceBus topologies.
- **`EricksonLopez.Outbox.Rebus` & `EricksonLopez.Outbox.Brighter`**: Superior transactional guarantees for lightweight messaging frameworks.
- **`EricksonLopez.Outbox.Dapr`**: Integration with Dapr's Pub/Sub building block for Cloud-Native/Kubernetes workloads.

### 2. CDC / Transaction Log Tailing
Currently, the Outbox uses Polling (via `AdaptivePoller`) or Listen/Notify (PostgreSQL). We plan to introduce true **Change Data Capture (CDC)**:
- **PostgreSQL WAL Tailing**: Reading the Write-Ahead Log directly using logical replication slots.
- **SQL Server CDC**: Integrating with SQL Server's native Change Data Capture to eliminate polling entirely.

### 3. Multi-Tenant Partitioning
Enhancing the `IOutboxRepository` to natively support multi-tenancy:
- Sharding outbox tables by `TenantId`.
- Routing messages to different broker connections based on the tenant context.

## 🟠 Medium-Term Goals

### 1. Saga / Process Manager Support
The current implementation handles event publication (Choreography). We aim to add lightweight Orchestration:
- `SagaStateMachine`: A durable state machine for long-running transactions.
- Compensation logic and Timeout management.

### 2. High-Performance Binary Serializers
Currently, the default is `System.Text.Json` (NativeAOT-friendly). We will add official plugins for:
- **Protobuf** (`protobuf-net`)
- **MessagePack** (`MessagePack-CSharp`)

## 🟣 Long-Term Vision (Enterprise Tooling)

### 1. Admin Dashboard UI
A standalone Blazor WebAssembly / ASP.NET Core dashboard to visualize the Outbox:
- View pending, failed, and dead-lettered messages.
- Manually trigger Replays for failed messages.
- View real-time Dispatcher telemetry (MPS - Messages Per Second).

### 2. Archival & Cleanup Service
An automated background service that safely moves successfully dispatched messages to "Cold Storage" (e.g., S3, Azure Blob Storage) for compliance and auditing, rather than aggressively deleting them (Delete-on-Dispatch).

## 🛑 Deferred to Next Major Release (v2.0)

Based on the architectural audit, the following structural improvements are deferred to the next
major release to preserve semantic versioning stability in v1.x:

### 1. Generic `IOutboxTransactionContext<TConnection, TTransaction>`
Currently, the transaction context relies on ADO.NET's `DbConnection` and `DbTransaction`. This blocks native implementations for NoSQL stores like Cosmos DB, MongoDB, and DynamoDB. In v2.0, this interface will become generic to support any backend's transaction primitive.

### 2. Move `IOutbox.Publish<T>` to Extension Method
The `IOutbox.Publish<T>` method currently returns a concrete `OutboxMessageBuilder<T>`. This makes mocking `IOutbox` difficult for unit testing without using the in-memory fakes. In v2.0, `Publish<T>` will be moved to a static extension method, keeping the core `IOutbox` interface clean and easily mockable via standard mocking libraries (Moq, NSubstitute).
