<!-- Copyright © Erickson Lopez. MIT License. -->

# ADR-031 — MongoDB Storage Strategy & NativeAOT Compatibility

## Status

Accepted

## Context

Several distributed systems teams utilize MongoDB as their primary operational datastore. To maintain dual-write consistency between MongoDB document mutations and outbound broker events, the Transactional Outbox pattern must be available on MongoDB.

However, MongoDB has distinct characteristics compared to relational databases:
1. Multi-document transactions (`IClientSessionHandle`) were introduced in MongoDB 4.0 and require a replica set or sharded cluster.
2. The official `MongoDB.Driver` historically relies on reflection and runtime IL generation for dynamic POCO BSON serialization, which presents challenges for NativeAOT compilation.
3. Concurrency control relies on atomic document operators (e.g., `FindOneAndUpdate`, optimistic concurrency filters, or locking status transitions) rather than SQL `SELECT ... FOR UPDATE SKIP LOCKED`.

## Decision

We introduce `EricksonLopez.Outbox.Storage.MongoDb` as a dedicated storage provider with the following architectural rules:

1. **Transaction Context Bridge**: `MongoDbTransactionContext` implements `IOutboxTransactionContext` and wraps `IClientSessionHandle`, allowing developers to enlist outbox writes within their existing MongoDB client session and transactions.
2. **NativeAOT Safe Payload Storage**: Outbox messages are persisted as BSON documents where payload data is pre-serialized into UTF-8 strings or binary byte arrays by the library's source-generated serializers. This completely avoids runtime reflection mapping of domain entities inside the MongoDB driver.
3. **Atomic State Transitions**: Dispatcher polling uses atomic `FindOneAndUpdate` with status filtering (`Pending`, `Dispatched`, `DeadLettered`) to ensure safe multi-instance concurrency without race conditions.
4. **Clean Decoupling**: MongoDB dependencies (`MongoDB.Driver`) are isolated strictly within `EricksonLopez.Outbox.Storage.MongoDb`, preserving zero external dependencies in the core `EricksonLopez.Outbox` package.

## Rationale

1. Enables MongoDB-first applications to benefit from the Transactional Outbox pattern with at-least-once delivery guarantees.
2. Preserves the library's core AOT-first principles by eliminating POCO reflection on the hot path.
3. Aligns with the multi-database provider ecosystem while maintaining explicit separation of concerns.

## Consequences

### Positive
- Full functional parity for MongoDB users in enterprise microservices.
- Seamless transactional atomicity when mutating MongoDB collections and saving outbox records.
- Zero impact on core library size or performance.

### Negative
- MongoDB transactions require replica set or sharded cluster configuration (standard MongoDB requirement for ACID multi-document transactions).

## Related ADRs

- ADR-027: Database Provider Tier Policy
- ADR-023: Serialization Pluggable AOT First
- ADR-026: No Reflection Based Discovery
