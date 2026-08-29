<!-- Copyright © Erickson Lopez. MIT License. -->

# ADR-020 — Core Outbox Does Not Depend On Specific Brokers

## Status

Accepted

## Context

The `EricksonLopez.Outbox` core package must be usable regardless of which message broker is in use. The question is whether broker-specific code should live in the core or in separate packages, and what the correct abstraction boundary is.

## Decision

The core `EricksonLopez.Outbox` package has zero dependency on RabbitMQ, Kafka, Azure Service Bus, AWS SQS, NATS, Redis Streams, Google Pub/Sub, or any other broker. All broker-specific code lives in `EricksonLopez.Outbox.Brokers.*` packages. The `IBrokerPublisher` interface in the core is the only broker abstraction, and it is intentionally minimal.

## Rationale

1. Broker coupling prevents switching brokers without changing the core package.
2. Transitive broker dependencies pollute applications that use a different broker.
3. Testing is dramatically simpler with a fake `IBrokerPublisher` — no real broker needed.
4. The Outbox concern is "durably store and eventually deliver" — it does not need to know the delivery protocol.
5. Different teams in an organization may use different brokers; the core must remain neutral.

## Alternatives Considered

### Alternative 1: Bundle RabbitMQ and Kafka in the core
Rejected: adds ~20MB of transitive dependencies to all consumers regardless of broker choice.

### Alternative 2: `IMessageTransport` with multiple built-in adapters
Rejected: equivalent to bundling broker code. The interface name doesn't change the coupling.

### Alternative 3: No broker abstraction — application provides the publish lambda
Partially considered: this would eliminate `IBrokerPublisher` entirely, but then the dispatcher cannot be provided by the library. The `IBrokerPublisher` abstraction is justified by the dispatcher needing a stable publish contract.

## Rejected Alternatives

Bundling any broker implementation in the core is rejected permanently.

## Consequences

### Positive
- Core package has minimal dependencies (only Microsoft.Extensions.Hosting.Abstractions, Diagnostics, ObjectPool)
- Trivial testing with `FakeBrokerPublisher`
- Broker-agnostic by construction

### Negative
- Users must reference at least one broker adapter package
- Breaking changes in `IBrokerPublisher` affect all broker packages simultaneously

## Ecosystem Impact

Each `EricksonLopez.Outbox.Brokers.*` package implements `IBrokerPublisher` for one specific broker. The `EricksonLopez.Outbox.MassTransit` package provides an adapter for MassTransit's own transport.

## Migration

No migration required. Existing packages already follow this pattern.

## Related ADRs

- ADR-016 (Outbox Is Not An Event Bus)
- ADR-022 (Serialization Is Pluggable, AOT-First)
