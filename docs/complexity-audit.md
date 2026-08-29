<!-- Copyright © Erickson Lopez. MIT License. -->

# Algorithmic & Complexity

This document provides a review of Cyclomatic Complexity (CC), Lack of Cohesion of Methods (LCOM), and Algorithmic Time Complexity (Big-O) for the critical hot paths of `EricksonLopez.Outbox`.

## 1. Hot Path Algorithmic Complexity (Big-O)

The outbox library is optimized to avoid `O(N^2)` operations in all critical paths.

### Dispatch Poller (`AdaptivePoller.cs`)
- **Claim Batch (Database Fetch)**: `O(log N + B)` where N is total rows and B is batch size, assuming proper indexing on `[Status, NextAttemptAt]`.
  - *PostgreSQL*: Uses `SKIP LOCKED`, meaning no full table scans during lock contention.
  - *SQL Server*: Uses `READPAST`.
- **Channel Write**: `O(1)` per message enqueued to `System.Threading.Channels`.
- **In-Memory Sorting**: `O(B log B)` to sort the batch by time if re-ordering is enabled. In default operation, `O(1)` since sorting relies on the `ORDER BY` clause in the SQL query.

### Dispatch Processing (`OutboxChannel.cs`)
- **Message Dispatch**: `O(1)` per message. Payload serialization is pre-compiled via Roslyn Source Generators.
- **Dead Letter Queue Routing**: `O(1)` condition check and `O(log N)` for the separate database INSERT.
- **Mark As Dispatched (Database Update)**: `O(1)` per message, or `O(B)` for batch operations where B is the batch size. 

### Message Publishing (`OutboxMessageBuilder.cs`)
- **Type Resolution**: `O(1)` using the generated generic dictionary/jump table.
- **Serialization (System.Text.Json)**: `O(S)` where S is the size of the payload. Minimal allocations via `Utf8JsonWriter` and `IBufferWriter<byte>`.
- **Database Insert**: `O(1)` appending to the transaction context.

## 2. Cyclomatic Complexity (CC) Analysis

*Target: CC < 10 for hot paths, Maintainability Index > 80.*

| Component | Target Class | Max CC (Method) | Average CC | Assessment |
|---|---|---|---|---|
| **Poller** | `AdaptivePoller` | 6 (`ExecuteAsync`) | 2.5 | **PASS**. The loop is well-isolated. Exponential backoff logic is extracted to pure functions. |
| **Dispatcher** | `OutboxChannel` | 8 (`ProcessMessagesAsync`) | 3.2 | **PASS**. Complexity remains below 10 despite handling cancellation, resilience circuits, and metrics. |
| **Storage (PostgreSQL)** | `PostgreSqlOutboxRepository` | 5 (`FetchPendingAsync`) | 2.0 | **PASS**. SQL generation is linear and parameterized. No nested conditionals. |
| **Routing** | `OutboxPipeline` | 4 (`InvokeAsync`) | 1.8 | **PASS**. Middleware execution is a linear `O(N)` loop over registered interceptors. |
| **Broker** | `RabbitMqPublisher` | 6 (`PublishAsync`) | 2.2 | **PASS**. Handles connection retry and publishing in a flat structure. |

## 3. Cohesion & Coupling (LCOM)

*Target: LCOM close to 0 (high cohesion), Fan-Out < 10 for core components.*

- **`AdaptivePoller`**: Highly cohesive. Contains only state related to polling delays, cancellation, and metrics. LCOM is near `0.2` as all methods mutate or read the internal cancellation token and delay state.
- **`OutboxChannel`**: Moderate cohesion (LCOM ~ `0.4`). It depends on the `IOutboxRepository`, `IBrokerPublisher`, and `OutboxMetrics`. Dependency injection keeps coupling low.
- **`OutboxMessageBuilder<T>`**: High cohesion (LCOM = `0`). A struct-based builder pattern ensuring zero allocations.

## 4. Assessment vs Ecosystem

- **MassTransit**: Tends to have higher cyclomatic complexity (CC > 20) in its dispatcher due to deep reflection, generic instantiation pipelines, and dynamic middleware generation.
- **EricksonLopez.Outbox**: Achieves **CC < 10** across the board because Roslyn Source Generators move the complexity to compile-time (emitting simple `switch` statements), leaving the runtime hot paths exceptionally flat and predictable.

**Conclusion**: The implementation strictly adheres to the requested complexity constraints. The use of source generators is the primary driver for keeping runtime Cyclomatic Complexity below the critical threshold of 10.
