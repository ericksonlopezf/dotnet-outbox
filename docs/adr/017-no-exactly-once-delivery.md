<!-- Copyright © Erickson Lopez. MIT License. -->

# ADR-017 — Outbox Does Not Guarantee Exactly-Once Delivery

## Status

Accepted

## Context

Users often expect transactional messaging to provide exactly-once delivery. This is especially true for financial or idempotency-sensitive workflows. The question is: can `EricksonLopez.Outbox` claim exactly-once delivery?

## Decision

`EricksonLopez.Outbox` provides **at-least-once delivery**. Exactly-once delivery is explicitly not claimed or implemented.

## Rationale

Exactly-once delivery across a relational database and a message broker requires two-phase commit (2PC) or a distributed transaction coordinator. No practical combination of PostgreSQL + RabbitMQ, PostgreSQL + Kafka, or PostgreSQL + Azure Service Bus supports 2PC in a way that is reliable and high-performance at scale.

**Precise technical distinctions:**
- **Exactly-once storage**: The `ON CONFLICT DO NOTHING` in `InsertAsync` provides idempotent write. Two concurrent callers with the same message ID will not create duplicates.
- **Exactly-once claiming**: `FOR UPDATE SKIP LOCKED` + `owner_id` ensures each message is claimed by at most one dispatcher instance at a time.
- **Exactly-once delivery to broker**: NOT GUARANTEED. A dispatcher crash after publishing to the broker but before `MarkAsDispatchedAsync` causes the message to be reclaimed and re-published.

The reclaim → re-publish cycle is the correct behavior for at-least-once semantics.

## Alternatives Considered

### Alternative 1: Add a distributed lock before publishing
Rejected: does not solve the fundamental problem. The crash can occur after the publish but before releasing the lock.

### Alternative 2: Use broker transactions (Kafka transactions, RabbitMQ confirms)
Rejected: moves complexity to the broker adapter layer; does not change the core Outbox guarantee. The core must not depend on specific broker capabilities.

### Alternative 3: Acknowledge-before-publish (at-most-once)
Rejected: the inverse failure mode — messages may be acknowledged in the DB but never actually published.

## Rejected Alternatives

All alternatives either require broker-specific coupling, change the guarantee to at-most-once (equally unacceptable), or require 2PC infrastructure that is impractical.

## Consequences

### Positive
- Honest contract; no false guarantees
- Simpler implementation
- Works with any broker regardless of transaction support

### Negative
- Consumers MUST implement idempotency
- Documentation must clearly warn about duplicate delivery in crash scenarios

## Ecosystem Impact

Consumer-side idempotency is addressed by `EricksonLopez.Outbox.Inbox`. The Inbox provides the deduplication mechanism that, combined with at-least-once delivery, achieves "effectively-once" processing.

## Migration

No migration required. This clarifies an existing behavior.

## Related ADRs

- ADR-016 (Outbox Is Not An Event Bus)
- ADR-021 (Consumer Idempotency Is A Separate Concern)
