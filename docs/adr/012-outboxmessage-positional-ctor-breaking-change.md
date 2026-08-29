<!-- Copyright © Erickson Lopez. MIT License. -->

# ADR-012: Breaking Change Risk — OutboxMessage Positional Record Constructor

## 1. Title and Status

**Binary Compatibility Evolution Strategy for `OutboxMessage`**
*Status:* Accepted — Risk Documented, Mitigation In Place

## 2. Context and Motivation

`OutboxMessage` is declared as a `sealed record` with a positional constructor. Adding a new positional parameter is a **source-breaking and binary-breaking change** because all call sites must be updated.

The type appears in: `IOutboxRepository`, `IBrokerPublisher.PublishRawAsync()`, `IOutboxMiddleware.InvokeAsync()`, and all user `IBrokerPublisher` implementations.

## 3. Decision

**Accept the constraint for v1.0.** Never add new positional parameters in v1.x. Use `Extensions` property or `init`-only properties for new fields.

## 4. Evolution Strategy — v1.x (Non-breaking additions)

New optional data MUST go through `OutboxMessage.Extensions`:

```csharp
// Already exists — zero breaking change:
public IReadOnlyDictionary<string, string>? Extensions { get; init; }
```

New convenience properties can be added as `init`-only via `partial record` in a new file:

```csharp
// OutboxMessage.v1_1.cs — additive, non-breaking
public sealed partial record OutboxMessage
{
    public string? TenantId => Extensions?.GetValueOrDefault("tenant_id");
}
```

## 5. v2.0 Migration Plan (Breaking)

If first-class fields are required, v2.0 will convert `OutboxMessage` to use `init`-only properties instead of positional constructor. A migration guide and multi-targeting will be provided.

## 6. Guidance for Contributors

**RULE**: Never add a positional parameter to `OutboxMessage` in a v1.x release.

## 7. Consequences

- Short term: `Extensions` escape hatch covers all v1.x extension needs.  
- Long term: v2.0 migration minimal — `DefaultOutbox.BuildOutboxMessage` is `internal`, so users rarely construct `OutboxMessage` directly.  
- AOT: Fully compatible regardless of evolution path.
