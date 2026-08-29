<!-- Copyright © Erickson Lopez. MIT License. -->

# ADR-022 — Consumer Idempotency (Inbox) Is A Separate Concern

## Status

Accepted

## Context

The current core `EricksonLopez.Outbox` package bundles `IInboxIdempotencyChecker`, `InboxIdempotencyChecker`, `InboxCleanupService`, `IIdempotencyRepository`, `OutboxInboxOptions`, `InboxConsumerAttribute`, and `IdempotentConsumerAttribute`. The question is whether consumer-side idempotency belongs in the Outbox core.

## Decision

Consumer-side idempotency (the Inbox pattern) is a separate concern and will be extracted into `EricksonLopez.Outbox.Inbox`. The core `EricksonLopez.Outbox` package will not register any Inbox services by default. A new `services.AddOutboxInbox()` extension will provide opt-in Inbox registration.

## Rationale

1. The Outbox is a **producer-side** concern: it ensures messages are published durably.
2. The Inbox is a **consumer-side** concern: it ensures messages are processed exactly once by a specific consumer.
3. A service that publishes messages via Outbox may not consume any messages — it does not need the Inbox.
4. A service that consumes messages may not publish via Outbox — it only needs the Inbox.
5. Bundling both in the core forces producers to download consumer infrastructure and vice versa.
6. The `outbox.idempotency` table is a separate concern from `outbox.messages`. They happen to share a schema prefix, but their lifecycle and semantics are independent.

## Alternatives Considered

### Alternative 1: Keep Inbox in the core but make it deeply opt-in (no default registration)
Partially considered: the types would still be in the same package. Accepted as an interim state if the package split is not feasible immediately.

### Alternative 2: Merge Inbox and Outbox into a unified "Outbox+Inbox" package
Rejected: conflates producer and consumer concerns. Users who only need one side pay the cost of the other.

## Rejected Alternatives

Alternative 2 is rejected. Alternative 1 is an acceptable interim state before the package split.

## Consequences

### Positive
- Clear separation of producer and consumer concerns
- Producers do not download consumer infrastructure
- Consumers can use Inbox independently of Outbox

### Negative
- Breaking change: users who registered Inbox via `AddOutbox()` must add `AddOutboxInbox()` call
- New package reference required for consumers

## Ecosystem Impact

`EricksonLopez.Outbox.Inbox` becomes a standalone NuGet package. It depends on `EricksonLopez.Outbox` (for shared types like `IOutboxTransactionContext`).

## Migration

1. Add `EricksonLopez.Outbox.Inbox` package reference to consumer services
2. Replace Inbox registration in `AddOutbox()` with explicit `services.AddOutboxInbox(options => { ... })`
3. No changes required for producer-only services

## Related ADRs

- ADR-017 (No Exactly-Once Delivery)
- ADR-020 (No Broker Dependency In Core)
