# ADR-003: Postgres SKIP LOCKED vs Traditional Polling

## 1. Title and Status
**Event Fetching Strategy: `FOR UPDATE SKIP LOCKED` (PostgreSQL)**
*Status:* Approved and Implemented in `EricksonLopez.Outbox.Storage`.

## 2. Context and Motivation
The Outbox pattern requires a background process (Fetcher/Dispatcher) that constantly reads the database table looking for messages with `state = 0` (Pending), publishes them to the Broker, and marks them as `1` (Processed).
If we implement a simple `SELECT ... FOR UPDATE` or `UPDATE ... RETURNING`, when we scale horizontally (multiple instances of the service running at once), the Workers will collide with each other, causing *Deadlocks*, high database contention, and severely degrading overall performance.

## 3. Evaluated Alternatives
1. **Advisory Locks (pg_advisory_lock):** Effective, but requires explicitly managing the lifecycle of connection-level locks, which is complex and prone to leaks if the Worker crashes.
2. **Leasing Table:** Using a `LockedUntil` field. It's easy to implement but suffers from latency (Polling delay) and is inefficient under extreme concurrency since several nodes might try to lock the same row simultaneously.
3. **Pure Trigger / LISTEN-NOTIFY:** Low latency, but vulnerable to massive traffic spikes where the notification buffer collapses.
4. **SKIP LOCKED (PostgreSQL >= 9.5):** Native solution from the relational engine.

## 4. Advantages
* **Perfect Concurrency:** `SELECT ... FOR UPDATE SKIP LOCKED` instantly ignores rows being read by another Worker. Multiple nodes consume the table in parallel without blocking.
* **Zero Collisions:** It is impossible for two Kubernetes pods to read and dispatch the same message simultaneously, achieving near-perfect "At-Least-Once" processing.
* **Infinite Horizontal Scalability:** If throughput increases, simply spin up more replicas of the Dispatcher.

## 5. Disadvantages
* **Compromised Agnosticism:** Not all SQL engines support `SKIP LOCKED` (e.g., SQLite does not, SQL Server uses `READPAST`, MySQL 8+ supports it).

## 6. Trade-offs
We decided to physically isolate the SQL dialect into engine-specific repository implementations (`PostgresOutboxRepository`). By not coupling the core, we abstract this problem away. For rudimentary engines like SQLite, a Leasing Table will be used as a fallback, but for high-demand environments (Postgres/SQL Server), the architecture pushes the hardware to its limit.

## 7. Performance Impact
* **Massive:** Eliminates the central bottleneck of the Outbox pattern (table contention). Enables thousands of dispatches per second.

## 8. NativeAOT Impact
* **Neutral:** The optimization happens entirely within the Database engine.

## 9. Maintainability Impact
* **Positive:** The SQL logic is trivial (just 3 extra words in the query) compared to managing logical leasing times.

## 10. Extensibility Impact
* **Neutral:** Aligns with our "Storage Agnostic" principle by demanding that Repository implementations manage their own concurrent fetching strategy opaquely to the Core.
