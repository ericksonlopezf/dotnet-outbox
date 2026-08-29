<!-- Copyright © Erickson Lopez. MIT License. -->

# ADR-035: MessageMetadata Persistence Struct Boundary vs Messaging Routing Record

## Status
Accepted — August 2026

## Context
`EricksonLopez.Outbox.Abstractions` defines `MessageMetadata` as a `readonly struct` to encapsulate database outbox message headers without allocating heap memory on high-throughput database operations.

An ecosystem audit reviewed the relationship between this struct and `EricksonLopez.Messaging.Abstractions.MessageMetadata`.

## Decision
Retain `EricksonLopez.Outbox.Abstractions.MessageMetadata` as an autonomous, persistence-optimized struct:

1. **Storage Optimization**:
   - Implemented as `readonly struct MessageMetadata(ReadOnlyMemory<MetadataEntry> entries)`.
   - Optimized for raw key-value pair serialization, deserialization, and column mapping across PostgreSQL, SQL Server, MySQL, SQLite, and MongoDB outbox tables.
   - Contains zero dependencies on message broker SDKs or distributed transport types.

2. **Decoupling from Transport**:
   - `Outbox.Abstractions.MessageMetadata` models raw stored metadata.
   - It does NOT replace or absorb the distributed routing semantics of `Messaging.Abstractions.MessageMetadata` (`sealed record`).

## Consequences
- **Zero-Allocation**: Guarantees zero heap allocation overhead during transactional outbox persistence and dispatch polling.
- **Provider Autonomy**: Database storage drivers can serialize and query metadata without pulling in messaging abstractions.
