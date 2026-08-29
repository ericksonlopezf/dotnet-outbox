<!-- Copyright © Erickson Lopez. MIT License. -->

# ADR-028 — Safe Default For ThrowOnUnregisteredType

## Status

Accepted

## Context

When an application stores an outbox message whose CLR type is not explicitly registered in the `IOutboxMessageTypeResolver`, two possible behaviors exist:
1. Fallback to `typeof(TMessage).Name`: Resolves the type name dynamically at runtime. This name is unstable across refactoring, namespace changes, and obfuscation, and violates strict NativeAOT principles if deserializers cannot dynamically resolve the type.
2. Fail-fast with `OutboxTypeNotRegisteredException`: Enforces that all message types are explicitly registered using stable aliases (e.g., via source-generated type mappers or fluent registration).

Previously, `ThrowOnUnregisteredType` had no explicit property initializer and defaulted to `false` (C# default).

## Decision

`OutboxRuntimeOptions.ThrowOnUnregisteredType` defaults to **`true`**.

If a message type is not registered in the configured `IOutboxMessageTypeResolver`, `StoreAsync` immediately throws `OutboxTypeNotRegisteredException`.

## Rationale

1. **Fail-safe by default:** Silently publishing messages with unstable CLR type names leads to critical downstream deserialization failures in message consumers across deployments.
2. **NativeAOT alignment:** Explicit registration is mandatory for reflection-free AOT compilation.
3. **Opt-in degraded mode:** Applications in rapid prototyping or non-AOT environments that specifically require the unstable fallback behavior must explicitly set `options.ThrowOnUnregisteredType = false`.

## Consequences

### Positive
- Prevents accidental production outages caused by renaming message classes.
- Guarantees 100% stable alias contracts across all publisher-subscriber boundaries.

### Negative
- Breaking behavioral change for codebases relying on implicit fallback naming without registering type aliases.

## Related ADRs

- ADR-004: Roslyn Source Generators AOT
- ADR-011: Source Generator JSON Context
- ADR-026: No Reflection-Based Discovery
