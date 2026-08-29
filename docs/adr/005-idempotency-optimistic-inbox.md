<!-- Copyright © Erickson Lopez. MIT License. -->

# ADR-005: Idempotency (Optimistic Inbox)

## 1. Title and Status
**Optimistic Inbox Pattern with Database Deduplication**
*Status:* Approved and Implemented in `EricksonLopez.Outbox` (Core).

## 2. Context and Motivation
The Outbox pattern guarantees that messages reach the broker (*At-Least-Once delivery*). However, this means that the consumer will occasionally receive the same event twice (due to broker retries, network ACK failures, etc.).
The motivation is to provide the framework with the reverse capability (*Inbox Pattern*) to guarantee *Exactly-Once* processing at the business logic level, cleanly discarding duplicate events.

## 3. Evaluated Alternatives
1. **In-Memory Deduplication (Cache/Redis):** Using `MemoryCache` or `Redis` to record IDs. Problem: If memory is used, multiple Workers will process duplicates. If Redis is used, a fragile external dependency (Distributed Lock) is introduced, which breaks the transactional concept.
2. **Heavy Inbox (Storing the full payload):** Saving the entire incoming message in an Inbox table and having a background Worker process it. Safe, but doubles I/O and storage.
3. **Optimistic Inbox (SQL Structural Deduplication):** Attempting to insert an `(MessageId, ConsumerId)` record within the same database transaction as the business logic. Relying on SQL's Primary Key Constraint to catch duplicates.

## 4. Advantages
* **Atomic Guarantee:** Since the Inbox record is saved in the same `IDbTransaction` as the affected domain entities, it is 100% impossible to process an event without leaving a record, or to leave a record without processing it.
* **Zero Extra Read I/O:** By relying on `INSERT ... ON CONFLICT DO NOTHING` (PostgreSQL), we avoid doing a prior `SELECT` to check if it exists. Everything is resolved in a single database roundtrip.

## 5. Disadvantages
* **Accumulated Garbage:** The Idempotency table will grow infinitely over time if not purged.

## 6. Trade-offs
We accept the table growth in exchange for absolute transactional security. It has been documented that Repository implementations (e.g., `PostgreSql`) must include a purge mechanism or "Time-To-Live" (TTL) in table migrations to clean up old idempotency records on a scheduled basis.

## 7. Performance Impact
* **Excellent:** Using native SQL conflict mitigation commands (`ON CONFLICT` in PG or `IGNORE` in others) avoids a prior `SELECT`, lowering consumer latency by 50% compared to a naive implementation (`if(!exists) insert;`).

## 8. Maintainability Impact
* **Positive:** The deduplication logic is cleanly wrapped in the `InboxIdempotencyChecker`, isolating domain developers from dealing with this in their Handlers.

## 9. Developer Experience (DX) Impact
* **Transparent:** The developer simply annotates their class with `[InboxConsumer]` and the middleware automatically intercepts the execution. If it is a duplicate, it short-circuits and silently returns an `ACK` to the broker.
