<!-- Copyright © Erickson Lopez. MIT License. -->

# ADR-026 — Outbox Does Not Use Reflection-Based Handler Discovery

## Status

Accepted

## Context

Some messaging frameworks (MassTransit, MediatR, Wolverine) use assembly scanning to discover message handlers via reflection at startup. The question is whether `EricksonLopez.Outbox` should use similar discovery mechanisms for type registration or handler wiring.

## Decision

`EricksonLopez.Outbox` uses zero reflection for type discovery. All type registration occurs through compile-time source generators (`[OutboxMessage("alias")]` attribute → Roslyn incremental generator → `FrozenDictionary` lookup). No assembly scanning, no `GetTypes()`, no `Activator.CreateInstance`, no `Expression.Lambda`, no reflection-based attribute reading at runtime.

## Rationale

1. Assembly scanning is fundamentally incompatible with NativeAOT and aggressive trimming. The linker cannot statically determine which types to preserve when types are discovered at runtime.
2. Reflection at startup increases cold-start time, which is critical for serverless and container cold-start scenarios.
3. Source generators provide all the benefits of automatic discovery (users don't manually register types) while preserving full AOT compatibility.
4. `FrozenDictionary` built at startup from source-generated data has O(1) average lookup with zero heap allocation on the hot path.
5. The source generator approach is validated: `OutboxTypeMappingGenerator` (Roslyn incremental generator) already implements this correctly.

## Alternatives Considered

### Alternative 1: Assembly scanning at startup
Rejected: incompatible with NativeAOT. The IL linker will trim types that are not statically referenced. Scanning would produce empty or incomplete results in trimmed builds.

### Alternative 2: Manual registration via `options.RegisterType<T>()`
Acceptable as a fallback: allows users who cannot use source generators to register types manually. But source generators remain the primary mechanism.

### Alternative 3: Runtime reflection with `[DynamicallyAccessedMembers]` annotations
Rejected: even with DynamicDependency annotations, the ergonomics are poor and the trimmer may not correctly preserve all required members across packages.

## Rejected Alternatives

Alternative 1 is permanently rejected. Alternative 3 is rejected as the primary mechanism (acceptable only as an opt-in escape hatch for exotic scenarios).

## Consequences

### Positive
- Full NativeAOT compatibility
- Zero startup reflection overhead
- O(1) type lookup on the hot path

### Negative
- Users must add `[OutboxMessage("alias")]` to their message types (small ergonomic cost)
- Source generators require Roslyn infrastructure; this adds tooling complexity for non-SDK-style projects (extremely rare)

## Ecosystem Impact

The `EricksonLopez.Outbox.SourceGenerators` package provides the Roslyn incremental generator. It must be referenced alongside `EricksonLopez.Outbox` in all consumer projects.

## Migration

No migration required. This has been the design from the beginning.

## Related ADRs

- ADR-023 (Serialization Is Pluggable, AOT-First)
- ADR-020 (No Broker Dependency In Core)
