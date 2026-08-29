<!-- Copyright © Erickson Lopez. MIT License. -->

# ADR-015: STJ `JsonSerializerContext` Auto-Generation — Roslyn Single-Pass Limitation

## Status

Accepted

## Context

The primary architectural objective of `EricksonLopez.Outbox` is to be zero-reflection and 100%
NativeAOT-compatible. As part of this goal, the source generator (`OutboxTypeMappingGenerator`)
was designed to discover all message types annotated with `[OutboxMessage]` and emit initialization code.

During the architectural audit (pre-v1.0 stabilization), a blocker was identified: users must manually
declare a `partial class` inheriting from `JsonSerializerContext` and decorate it with
`[JsonSerializable(typeof(T))]` attributes for each message type. The proposed improvement was for
`OutboxTypeMappingGenerator` to auto-generate this class, eliminating boilerplate entirely.

## Decision

An attempt was made to implement auto-generation of the `JsonSerializerContext` class, but the
approach encountered a **deep architectural constraint in Roslyn**:

- .NET Source Generators (including `IIncrementalGenerator`) run in a **single compilation pass**.
- Microsoft's native generator (`System.Text.Json.SourceGeneration`) cannot "see" code emitted by
  other generators during the same compilation pass.
- Therefore, if `OutboxTypeMappingGenerator` emitted the class decorated with `[JsonSerializable]`,
  the STJ source generator would not process those attributes — causing compilation errors due to
  unimplemented interface members (`GetTypeInfo`, etc.) in the generated class.

**Decision**: Do **not** attempt to force auto-generation of `JsonSerializerContext` via compiler workarounds.
Maintain the design where the consumer provides the JSON context manually by copying the template
generated at `obj/OutboxJsonContext.g.cs`.

> See also: [Roslyn issue #57239](https://github.com/dotnet/roslyn/issues/57239) — the fundamental
> Roslyn limitation that prevents cross-generator source consumption in a single compilation pass.

## Mitigation

The generator emits `OutboxRegistrationExtensions.Context.g.cs` as a **commented template** that the
consumer copies and customizes. Additionally, the `UseGeneratedTypes(JsonSerializerContext)` overload
was introduced to allow straightforward registration of the consumer's own context:

```csharp
// Step 1: Copy the template from obj/OutboxJsonContext.g.cs into your project
// Step 2: Annotate your message types
[JsonSerializable(typeof(OrderPlacedEvent))]
[JsonSerializable(typeof(PaymentProcessedEvent))]
public partial class MyOutboxJsonContext : JsonSerializerContext { }

// Step 3: Register the context
builder.Services.AddOutbox(options =>
{
    options.UseSerializer(new NativeAotJsonSerializer(MyOutboxJsonContext.Default));
    options.UseGeneratedTypes(MyOutboxJsonContext.Default);
});
```

## Consequences

### Negative
- Library consumers in NativeAOT mode must perform an additional manual step for each new message type
  (adding `[JsonSerializable]` to their context class and recompiling).

### Positive
- Avoids fragile coupling to the Roslyn compilation pipeline.
- Avoids breaking the MSBuild incremental build flow.
- Maintains fast, cache-friendly incremental compilation (no double passes required).
- The design is fully transparent: the generated template at `obj/OutboxJsonContext.g.cs` provides
  actionable copy-paste guidance and explains the Roslyn constraint with a link to the upstream issue.

## Related

- [ADR-004](004-roslyn-source-generators-aot.md) — Roslyn Source Generators for AOT
- [ADR-011](011-source-generator-json-context.md) — Source Generator JSON Context (Consumer-side validation)
