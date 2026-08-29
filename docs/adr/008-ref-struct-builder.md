# ADR-008: `ref struct OutboxMessageBuilder` for a Zero-Allocation Fluent API

## 1. Title and Status
**Zero-Allocation Fluent Builder (HISTORICAL)**
*Status:* ~~Approved and Implemented~~ **SUPERSEDED by [ADR-037](037-outboxmessagebuilder-sealed-class-rationale.md)**

> [!WARNING]
> This ADR is **superseded**. The implementation was changed from `ref struct` to `sealed class : IDisposable`
> before the v1.0.0 release. This document is preserved as historical context only.
> See [ADR-037](037-outboxmessagebuilder-sealed-class-rationale.md) for the current rationale.

## 2. Context and Motivation
The fluent API `outbox.StoreAsync(event).WithTransaction(tx).WithHeader(...).SaveAsync()` can generate allocations if the builder is a `class`. In the hot path (thousands of messages per second), this generates significant GC pressure.

## 3. Decision
Implement `OutboxMessageBuilder<TMessage>` as a `ref struct`.

## 4. Rationale
A `ref struct`:
- **Cannot escape to the heap** — the CLR guarantees this at compile time.
- **Zero allocation** — it lives entirely in the stack frame of the calling method.
- **Cannot be boxed** — it is impossible to cast it to `object`, `IDisposable`, etc.
- **Cannot be captured in lambdas** — the compiler rejects it.

For the builder's use case (fluent chain → `await SaveAsync()`), these restrictions are acceptable:

```csharp
await outbox
    .StoreAsync(new OrderCreatedEvent(...))  // → ref struct on the stack
    .WithTransaction(tx)                     // → modifies the stack frame, returns this
    .WithHeader("TenantId", tenantId)        // → same
    .SaveAsync(ct);                          // → await here, the builder is discarded
```

## 5. Optimization: Fast Path vs Metadata Path
The metadata dictionary is only instantiated when the consumer actually adds headers. In 80% of cases, there are no custom headers, resulting in strictly zero extra allocations.

## 6. Trade-offs

| Aspect | `ref struct` | `class` builder |
|---------|-----------|---------------|
| Allocation | Zero | 1 heap object |
| Capture in lambda | ❌ No | ✅ Yes |
| Direct `await` | ✅ Yes | ✅ Yes |
| Interface implementation | ❌ No | ✅ Yes |
| NativeAOT | ✅ Yes | ✅ Yes |

## 7. Consequences
- Developers cannot save the builder in a long-lived field or variable. This is intentional — the builder must be used in a fluent chain and discarded.
- The compiler produces a clear error if there is an attempt to capture the builder in a lambda.
- The hot path has zero allocation overhead for the builder itself.
