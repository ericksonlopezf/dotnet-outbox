<!-- Copyright © Erickson Lopez. MIT License. -->

# ADR-023 — Outbox Serialization Is Pluggable, AOT-First

## Status

Accepted

## Context

The Outbox must serialize message payloads to store them in the database. The question is: which serialization format should the Outbox depend on, and how should this be structured for NativeAOT compatibility?

## Decision

The Outbox provides `IOutboxSerializer` as the serialization abstraction. The default implementation uses `System.Text.Json` with source-generated `JsonSerializerContext` (no reflection). Users must provide a source-generated `JsonTypeInfo<T>` context via `options.UseSerializer(new NativeAotJsonSerializer(context))`. The core has no hard dependency on any serialization library — `System.Text.Json` is a default implementation, not a mandated dependency.

## Rationale

1. NativeAOT requires all type metadata to be resolved at compile time. Reflection-based serializers (`JsonSerializer.Serialize(obj)`) are incompatible.
2. Source-generated `JsonSerializerContext` provides full AOT compatibility via compile-time code generation.
3. Different applications may prefer different serialization formats (protobuf, MessagePack). The abstraction supports this.
4. The serialization format is not a core Outbox concern — the Outbox only cares about `byte[]` payloads.
5. Payload format changes (schema evolution) are handled at the application/Events layer, not the Outbox.

## Alternatives Considered

### Alternative 1: Hard-code System.Text.Json with reflection
Rejected: breaks NativeAOT and trimming. IL2026/IL3050 warnings in publishable binaries.

### Alternative 2: Use Newtonsoft.Json
Rejected: reflection-based, incompatible with NativeAOT, legacy dependency.

### Alternative 3: Use MessagePack as the default
Rejected: requires an additional NuGet dependency and attribute decorations on message types. STJ source-gen is already built into .NET.

## Rejected Alternatives

Any reflection-based serialization approach is permanently rejected. It is incompatible with the AOT-first principle.

## Consequences

### Positive
- Full NativeAOT compatibility
- Zero heap allocation during serialization (ThreadStatic buffer reuse)
- Users can plug in protobuf/MessagePack via `IOutboxSerializer`

### Negative
- Users must provide a source-generated `JsonSerializerContext` — more explicit setup
- Cannot use `JsonSerializer.Serialize(obj)` directly without source gen

## Ecosystem Impact

`EricksonLopez.Outbox.SourceGenerators` generates the type resolver; the user provides the serialization context. The two are decoupled.

## Migration

No migration for new users. Existing users already use `NativeAotJsonSerializer` with a custom context.

## Related ADRs

- ADR-024 (No Reflection-Based Handler Discovery)
- ADR-021 (No IIntegrationEvent In Outbox)
