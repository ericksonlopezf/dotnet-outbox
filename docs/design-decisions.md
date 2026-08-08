# Architecture Decision Records — Index

This document is an **index** of the Architecture Decision Records (ADRs) for
`EricksonLopez.Outbox`. Each ADR documents a significant technical decision,
its context, alternatives considered, and tradeoffs accepted.

The authoritative ADR content lives in [`docs/adr/`](adr/).

---

## ADR Status Definitions

| Status | Meaning |
|---|---|
| **Proposed** | Under discussion — not yet implemented |
| **Approved** | Accepted — implemented in the codebase |
| **Superseded** | Replaced by a newer ADR |
| **Deprecated** | No longer relevant |

---

## ADR Registry

| ADR | Title | Status | Key Decision |
|---|---|---|---|
| [ADR-001](adr/001-monorepo_modular_structure.md) | Monorepo Modular Structure | **Superseded** by ADR-009 | Original 6-project consolidated model |
| [ADR-002](adr/002-zero_allocation_models.md) | Zero-Allocation Models | Approved | `readonly record struct` + `ValueTask` throughout |
| [ADR-003](adr/003-postgresql_skip_locked.md) | PostgreSQL `SKIP LOCKED` | Approved | Lock-free concurrent polling via `FOR UPDATE SKIP LOCKED` |
| [ADR-004](adr/004-roslyn_source_generators_aot.md) | Roslyn Source Generators for AOT | Approved | Compile-time type mapping — zero runtime Reflection |
| [ADR-005](adr/005-idempotency_optimistic_inbox.md) | Optimistic Inbox Idempotency | Approved | `INSERT ... ON CONFLICT DO NOTHING` for deduplication |
| [ADR-006](adr/006-bounded_channels_dispatcher.md) | Bounded Channels Dispatcher | Approved | `System.Threading.Channels` for backpressure-aware dispatch |
| [ADR-007](adr/007-outboxmessage_readonly_record_struct.md) | `OutboxMessage` as `readonly record struct` | Approved | Stack-based, immutable, value-equality for the core data type |
| [ADR-008](adr/008-ref_struct_builder.md) | `ref struct OutboxMessageBuilder` | Approved | Zero-allocation fluent builder guaranteed to stay on the stack |
| [ADR-009](adr/009-package_consolidation_strategy.md) | Package Consolidation Strategy | Approved | Per-provider packages (17 projects) instead of consolidated |
| [ADR-010](adr/010-remove_dapper_raw_adonet.md) | Remove Dapper, Adopt Raw ADO.NET | Approved | Zero-allocation storage via raw `DbCommand`/`DbDataReader` |
| [ADR-011](adr/011-source_generator_json_context.md) | Source Generator JSON Context Limitation | Approved | Consumer-declared `JsonSerializerContext` with Analyzer validation |
| [ADR-012](adr/012-outboxmessage-positional-ctor-breaking-change.md) | `OutboxMessage` Binary Compatibility Evolution Strategy | Approved | Never add positional ctor params in v1.x; use `Extensions` or `init`-only properties |
| [ADR-013](adr/013-stryker-mutation-exclusions.md) | Stryker Mutation Testing — Exclusion Rationale | Approved | Logging, guard clauses, and generated code excluded from mutation scope |
| [ADR-014](adr/014-stryker-exclude-integration-tests.md) | Exclude Integration Tests from Stryker Mutation Scope | Approved | Integration tests excluded from Stryker to prevent false positives and timeouts |
| [ADR-015](adr/015-limitation-source-generator-json.md) | STJ `JsonSerializerContext` Auto-Generation — Roslyn Single-Pass Limitation | Approved | Roslyn single-pass constraint prevents cross-generator STJ integration; consumer must declare context manually |
| [ADR-016](adr/016-changelog-version-hygiene.md) | CHANGELOG Version Hygiene — Removal of Fabricated Release History | Approved | Only Release Please-managed or git-tag-verified entries may appear as release sections in CHANGELOG.md |

---

## Key Design Principles (Summary)

The following principles are the common thread across all ADRs:

1. **Zero Reflection** — All type resolution happens at compile time via Source Generators.
2. **Zero Allocation on Hot Paths** — `ref struct` builders, `readonly record struct` models, `ArrayPool<T>`, `ValueTask`.
3. **Raw ADO.NET First** — Pure `DbCommand`/`DbDataReader` with `FOR UPDATE SKIP LOCKED` is the canonical storage path (Dapper was removed per ADR-010).
4. **Delete-on-Dispatch** — Successfully dispatched messages are deleted (not soft-deleted) to prevent table bloat.
5. **NativeAOT by Default** — Every design decision is evaluated against NativeAOT compatibility first.
6. **Modularity** — Consumers install only what they need; per-provider packages have minimal transitive dependency footprints (per ADR-009).

For detailed rationale, tradeoffs, and consequences of each decision, read the individual ADR documents linked above.
