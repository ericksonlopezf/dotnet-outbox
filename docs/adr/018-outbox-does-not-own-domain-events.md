<!-- Copyright © Erickson Lopez. MIT License. -->

# ADR-018 — Outbox Does Not Own Domain Events

## Status

Accepted

## Context

Some Outbox implementations store Domain Events directly. The question is whether `EricksonLopez.Outbox` should accept Domain Events as input or whether it should only accept Integration Events (or generic serializable messages).

## Decision

`EricksonLopez.Outbox` does not own or manage Domain Events. It accepts any serializable message type via `IOutbox.StoreAsync<T>()` without requiring the message to implement any specific Outbox-owned interface. The mapping from Domain Events to Integration Events (or any external message form) is the Application layer's responsibility.

## Rationale

1. Domain Events are internal facts that occurred within a bounded context. They belong to the domain model and are defined in the domain or Events library.
2. Integration Events are external contracts published to other bounded contexts. They are defined in shared contract libraries or the Events library.
3. The Outbox must not force users to implement `IOutboxMessage`, `IIntegrationEvent`, or any Outbox-owned interface on their domain model. This would couple the domain to the messaging infrastructure.
4. The type-alias system (`[OutboxMessage("alias")]` attribute) is the only Outbox contract that touches message types, and it is a compile-time attribute with no runtime interface constraint.

## Alternatives Considered

### Alternative 1: Accept only `IIntegrationEvent`
Rejected: forces domain types to implement an Outbox-owned interface. Violates DDD layering.

### Alternative 2: `IOutboxMessage` marker interface
Rejected: same problem. The Outbox should be indifferent to what it stores as long as it can serialize it.

### Alternative 3: Remove `IIntegrationEvent` from `Outbox.Contracts`
Accepted as a **required action**: `IIntegrationEvent` currently in `EricksonLopez.Outbox.Contracts` must be moved to `EricksonLopez.Events`. The `DefaultOutbox.BuildOutboxMessage` check against `IIntegrationEvent` must be removed.

## Rejected Alternatives

Any mechanism that requires message types to implement an Outbox-owned interface is rejected.

## Consequences

### Positive
- Clean DDD layering: domain is unaware of Outbox
- Any POCO can be stored in the Outbox
- Outbox can be adopted without changing existing domain models

### Negative
- Application layer must explicitly map Domain Events to the message form before calling `StoreAsync`
- Slightly more boilerplate for the first integration

## Ecosystem Impact

`IIntegrationEvent` moves to `EricksonLopez.Events`. The Outbox has no dependency on Events at compile time.

## Migration

Remove `IIntegrationEvent` check from `DefaultOutbox.BuildOutboxMessage`. Users who relied on automatic `EventId` extraction from `IIntegrationEvent` must pass the ID explicitly via `MessageMetadata` or generate it in the Outbox unconditionally (recommended: `Guid.CreateVersion7()`).

## Related ADRs

- ADR-016 (Outbox Is Not An Event Bus)
- ADR-017 (No Exactly-Once Delivery)
