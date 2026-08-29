<!-- Copyright © Erickson Lopez. MIT License. -->

# ADR-016 — Outbox Is Not An Event Bus

## Status

Accepted

## Context

There is frequent pressure to add in-process event dispatching capabilities to the Outbox, blurring the boundary between the Transactional Outbox pattern and an Event Bus. Users often want to dispatch Domain Events in-process AND persist an outbox message in the same call. The question is whether `EricksonLopez.Outbox` should provide or own the in-process dispatch mechanism.

## Decision

`EricksonLopez.Outbox` does not implement in-process event dispatch. It stores serialized, already-formed messages to the database within the caller's transaction. The caller is responsible for forming the message before calling `IOutbox.StoreAsync<T>()`. No handler discovery, no in-process routing, no subscription mechanism.

## Rationale

The Outbox's sole responsibility is eliminating the Dual Write Problem by making message persistence atomic with business state changes. In-process dispatch is a separate concern solved by `EricksonLopez.Mediator` or the application layer. Conflating the two creates:
1. A dependency cycle risk between Mediator and Outbox
2. A violation of Single Responsibility
3. Confusion about when events are "dispatched" vs "persisted"
4. Ordering ambiguity: should the in-process handler run before or after the DB commit?

## Alternatives Considered

### Alternative 1: `IOutbox.Dispatch<T>(event)` — dispatch in-process AND persist
Rejected because it requires the Outbox to know about handler registration, DI resolution of handlers, and ordering semantics. This is the Mediator's domain.

### Alternative 2: `IEventPublisher` combining in-process and outbox
Rejected because it creates a leaky abstraction that forces consumers to couple their handlers to persistence infrastructure.

## Rejected Alternatives

Both alternatives above were rejected. The correct composition is:
```
MediatorPublisher.Publish(domainEvent);  // in-process
outbox.StoreAsync(integrationEvent, tx); // outbox persistence
```
These are two separate calls from the application layer, not one combined operation from the Outbox.

## Consequences

### Positive
- Clear boundary: Outbox is purely a durability primitive
- No dependency cycle with Mediator
- Consumers can use Outbox without any in-process dispatch
- Simpler public API

### Negative
- Application layer must wire up Mediator → Outbox explicitly
- Slightly more boilerplate for common patterns (mitigated by application-layer helpers)

## Ecosystem Impact

`EricksonLopez.Mediator` owns in-process dispatch. The `EricksonLopez.Outbox.MediatR` adapter package provides the composition pattern for MediatR users, but the core Outbox has no knowledge of it.

## Migration

No migration required. This is a non-feature (something not added).

## Related ADRs

- ADR-017 (Outbox Does Not Own Domain Events)
- ADR-020 (No Reflection-Based Handler Discovery)
