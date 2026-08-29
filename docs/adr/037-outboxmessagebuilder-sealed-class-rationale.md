<!-- Copyright © Erickson Lopez. MIT License. -->

# ADR-037: `OutboxMessageBuilder<T>` as `sealed class : IDisposable` (Supersedes ADR-008)

## Status

Accepted — August 2026

> **Supersedes:** [ADR-008](008-ref-struct-builder.md) (ref struct builder — original intent, not implemented)

## Context

[ADR-008](008-ref-struct-builder.md) documented the original architectural intent to implement
`OutboxMessageBuilder<TMessage>` as a `ref struct` to guarantee zero heap allocation in the fluent chain.

During pre-v1.0.0 implementation, two constraints made `ref struct` untenable:

1. **`async`/`await` Incompatibility**: `ref struct` types cannot be used across `await` suspension points
   in `async` state machines. The `StoreAsync(CancellationToken)` method — the terminal step of the
   fluent chain — is an `async` method. A `ref struct` builder would require the entire chain to be
   written without `await`, or would require a completely different pattern (e.g., `IAsyncDisposable`
   on a non-ref type).

2. **`ArrayPool<MetadataEntry>` Lifetime**: The builder rents a `MetadataEntry[]` buffer from
   `ArrayPool<MetadataEntry>.Shared` when `WithHeader()` is called. Returning the rented buffer requires
   a deterministic cleanup guarantee (`IDisposable`). `ref struct` cannot implement interfaces, so it
   cannot implement `IDisposable`. Without `IDisposable`, the rented buffer can only be returned inside
   `StoreAsync()`, which is only reliable if the caller always completes the chain.

## Decision

Implement `OutboxMessageBuilder<TMessage>` as a `sealed class` implementing `IDisposable`.

```csharp
public sealed class OutboxMessageBuilder<TMessage> : IDisposable where TMessage : notnull
{
    private bool _disposed;
    private MetadataEntry[]? _headersArray; // rented from ArrayPool<MetadataEntry>.Shared
    private int _headerCount;
    // ...

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_headersArray is not null)
            ArrayPool<MetadataEntry>.Shared.Return(_headersArray);
    }
}
```

`StoreAsync()` disposes the builder before returning:

```csharp
public async ValueTask StoreAsync(CancellationToken cancellationToken = default)
{
    // ... validation, store, ...
    Dispose(); // return rented buffer
}
```

## Rationale

| Aspect | `ref struct` (ADR-008 intent) | `sealed class` (implemented) |
|---|---|---|
| `await` across suspension points | ❌ Not allowed | ✅ Fully supported |
| `IDisposable` for ArrayPool return | ❌ Cannot implement interfaces | ✅ Implemented |
| Heap allocation | Zero | 1 object (builder only) |
| `ArrayPool` buffer overhead | N/A (no buffer in ref struct) | Zero on header-free paths (`_headersArray` is null) |
| Capture in lambda | ❌ No (restricted) | ✅ Yes |
| NativeAOT | ✅ | ✅ |

### Allocation Analysis

In the 80% fast path (no custom headers), the builder allocates only the single `OutboxMessageBuilder<T>`
object on the heap. `_headersArray` remains `null` and no `ArrayPool` rental occurs. The GC overhead of
a single short-lived small object per `StoreAsync()` call is negligible.

In the header path (`WithHeader()` / `WithTenantId()` / `WithCorrelationId()` / `WithCausationId()`),
the `MetadataEntry[]` array is rented from `ArrayPool<MetadataEntry>.Shared`, used within the async
state machine frame, and returned to the pool on `Dispose()`. This achieves near-zero allocation on
repeated calls.

## Consequences

### Positive
- Full `async`/`await` support in the fluent terminal step.
- Deterministic `IDisposable`-based buffer return to `ArrayPool`.
- The caller can `await` within the chain without restrictions.
- `using` statement or `await using` can be used if the chain is abandoned without calling `StoreAsync()`.

### Negative
- One heap allocation per `Publish()` call (the builder object itself).
- Consumers must be aware that the builder must be disposed if the chain is abandoned early.
  The compiler does **not** enforce this — it is documented in the XML summary and the API reference.

### Migration from ADR-008

No user migration is required. The change from `ref struct` to `sealed class` was made before the
v1.0.0 public release. No API surface was published under the `ref struct` type.

## Related ADRs

- [ADR-008](008-ref-struct-builder.md) — Original ref struct intent (now SUPERSEDED)
- [ADR-002](002-zero-allocation-models.md) — Zero-allocation models (builder meets spirit of this ADR)
- [ADR-004](004-roslyn-source-generators-aot.md) — NativeAOT compatibility
