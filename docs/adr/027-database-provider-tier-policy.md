<!-- Copyright © Erickson Lopez. MIT License. -->

# ADR-027 — Database Provider Tier Policy

## Status

Accepted

## Context

`EricksonLopez.Outbox` provides storage providers for multiple databases: PostgreSQL, SQL Server, MySQL, SQLite, and Oracle. However, these database engines have vastly different concurrency, notification, and bulk-processing capabilities. PostgreSQL supports unique primitives (`FOR UPDATE SKIP LOCKED`, `UNNEST` array batching, `LISTEN / NOTIFY` reactive wakeup, and `BINARY COPY` bulk insert) that enable orders-of-magnitude higher throughput and lower MVCC bloat than generic SQL alternatives.

Attempting to treat all relational providers with identical feature parity leads to a lowest-common-denominator architecture that compromises the performance mission of the library.

## Decision

We establish an explicit two-tier database provider policy:

| Tier | Databases | Commitment & Capabilities |
|---|---|---|
| **Tier-1 (Production Reference)** | PostgreSQL | Full feature set, maximum optimization (`UNNEST`, `SKIP LOCKED`, `LISTEN/NOTIFY`, `BINARY COPY`, partition range hints, MVCC autovacuum tuning), first-class benchmarks, primary maintenance focus. |
| **Tier-2 (Community Support)** | SQL Server, MySQL, SQLite, Oracle | Functional outbox storage and idempotency repositories, standard SQL concurrency primitives, integration test coverage, community PRs accepted. Advanced engine-specific optimizations (like PostgreSQL array unnesting) are not ported to Tier-2 engines unless native equivalents exist. |

## Rationale

1. PostgreSQL provides unmatched native capabilities for the transactional outbox pattern.
2. Setting honest expectations prevents user surprises regarding performance characteristics across different database backends.
3. Allows deep optimization of the PostgreSQL path without being constrained by SQL Server, MySQL, or Oracle limitations.

## Consequences

### Positive
- PostgreSQL users get industry-leading outbox performance (zero reflection, single-statement batch inserts via UNNEST, instant wakeup via LISTEN/NOTIFY).
- Clean architectural boundaries for non-PostgreSQL providers.

### Negative
- Non-PostgreSQL providers have higher query round-trips for batch inserts (row-by-row parameterized queries) and polling intervals without reactive notifications.

## Related ADRs

- ADR-003: PostgreSQL SKIP LOCKED
- ADR-010: Remove Dapper Raw ADO.NET
