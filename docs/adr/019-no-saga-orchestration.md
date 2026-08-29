<!-- Copyright © Erickson Lopez. MIT License. -->

# ADR-019 — Outbox Does Not Implement Saga Orchestration

## Status

Accepted

## Context

roadmap.md previously listed "Saga / Process Manager Support" as a medium-term goal with `SagaStateMachine`, compensation logic, and timeout management. The question is whether this belongs in the Outbox.

## Decision

Saga orchestration is removed from the roadmap and formally excluded from `EricksonLopez.Outbox`. This decision is permanent.

## Rationale

1. Sagas are complex workflow engines with their own state persistence requirements, state machine semantics, and compensation logic. They are fundamentally different from the Transactional Outbox pattern.
2. The Outbox is a publish primitive — it makes a single message durable and delivers it. It has no concept of "waiting for a reply" or "compensating on timeout."
3. Adding saga support would transform a focused, minimal library into a framework, increasing surface area, complexity, and maintenance burden by an order of magnitude.
4. Existing solutions (MassTransit Sagas, Wolverine Saga, NServiceBus Saga) already solve this well and are mature. There is no competitive advantage to replicating them.
5. Including Sagas would require the Outbox to manage multiple correlated messages, state transitions, timers, and compensation — all of which are out of scope.

## Alternatives Considered

### Alternative 1: Lightweight saga with `SagaStateMachine`
Rejected: "lightweight saga" is an oxymoron once you need compensation and timeout. The complexity grows unboundedly.

### Alternative 2: Saga via outbox-stored state transitions
Rejected: this is still a saga engine; the fact that it uses the outbox table for storage doesn't change the conceptual boundary.

## Rejected Alternatives

Both alternatives expand the library beyond its defined scope. They are rejected permanently.

## Consequences

### Positive
- Outbox remains focused and minimal
- Reduced maintenance burden
- Clear boundary: Outbox is a primitive, not a framework

### Negative
- Users needing saga orchestration must combine Outbox with a separate saga library
- Some integration scenarios require more explicit wiring

## Ecosystem Impact

Users who need saga orchestration should use: MassTransit Sagas (with `EricksonLopez.Outbox.MassTransit`), Wolverine, or NServiceBus Sagas.

## Migration

Remove Saga-related content from roadmap.md. No code migration required (nothing was implemented).

## Related ADRs

- ADR-016 (Outbox Is Not An Event Bus)
- ADR-023 (Outbox Does Not Become A Scheduler)
