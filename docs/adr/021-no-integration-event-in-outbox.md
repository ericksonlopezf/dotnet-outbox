<!-- Copyright © Erickson Lopez. MIT License. -->

# ADR-021 — IIntegrationEvent Is Not An Outbox Contract

## Status

Accepted — Pending Implementation

## Context

`EricksonLopez.Outbox.Contracts.IIntegrationEvent` currently defines `EventId` and `OccurredOn`. `DefaultOutbox.BuildOutboxMessage` checks `message is IIntegrationEvent ev` to extract `EventId`. This creates a coupling from the Outbox core to an Events contract abstraction.

## Decision

`IIntegrationEvent` will be removed from `EricksonLopez.Outbox.Contracts` and moved to `EricksonLopez.Events`. The `DefaultOutbox.BuildOutboxMessage` will not contain any type check against `IIntegrationEvent` or any other domain contract interface.

The Outbox will generate a new `Guid.CreateVersion7()` (or `Guid.NewGuid()` on .NET 8) unconditionally as the message ID, rather than extracting it from the message.

## Rationale

1. The Outbox should accept **any** serializable message type. Checking for `IIntegrationEvent` couples the hot write path to a specific message contract.
2. The `EventId` is the Outbox message's `Id` — the Outbox generates it. If the caller wants to use a specific ID (for idempotency), they should pass it via `MessageMetadata` or via the `OutboxMessageBuilder`.
3. `OccurredOn` has no meaning to the Outbox; `created_at` is the Outbox-owned timestamp.
4. The check `message is IIntegrationEvent` uses `is` pattern matching which requires the CLR to load and compare the type — while not strictly reflection, it creates a conceptual dependency from Outbox to Events.

## Alternatives Considered

### Alternative 1: Keep `IIntegrationEvent` but move it to a shared location
Rejected: still creates a conceptual dependency. The Outbox must not know what an "Integration Event" is.

### Alternative 2: Marker attribute instead of interface
Rejected: the Outbox already has `[OutboxMessage("alias")]` for type registration. A second attribute for ID extraction adds complexity without benefit.

### Alternative 3: `IHasEventId` optional interface checked at runtime
Rejected: similar problem. Outbox should not check for any caller-owned interface.

## Rejected Alternatives

Any mechanism that requires the Outbox to inspect the message type for a caller-defined interface is rejected.

## Consequences

### Positive
- Outbox accepts truly any POCO without interface requirements
- Hot path no longer has a type check

### Negative
- Breaking change for users who relied on automatic `EventId` extraction
- Users must either generate IDs themselves or let the Outbox generate them

## Ecosystem Impact

`IIntegrationEvent` moves to `EricksonLopez.Events`. Applications that use both libraries reference `EricksonLopez.Events` for the interface definition.

## Migration

Users who implemented `IIntegrationEvent` on their message types and relied on automatic ID extraction must:
1. Either let the Outbox generate a new ID unconditionally (change is transparent)
2. Or pass a specific ID via `outbox.Publish<T>(message).WithId(eventId).StoreAsync(tx)`

## Related ADRs

- ADR-018 (Outbox Does Not Own Domain Events)
- ADR-022 (Serialization Is Pluggable, AOT-First)
