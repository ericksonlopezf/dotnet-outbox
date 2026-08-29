<!-- Copyright © Erickson Lopez. MIT License. -->

# ADR-029 — DeadLetterRepository Standalone Transaction Boundary

## Status

Accepted

## Context

When the outbox background dispatcher exhausts the maximum retry count for a poisoned or failing message, it moves the message to the dead-letter repository (`IDeadLetterRepository`). Unlike application-layer `StoreAsync` calls that occur inside the caller's business transaction (`DbTransaction`), dead-lettering occurs inside a background dispatcher loop where no ambient transaction is present.

## Decision

`IDeadLetterRepository.InsertAsync` accepts an optional transaction context (`IOutboxTransactionContext? transaction = null`). When `transaction` is null, the repository opens its own dedicated connection, inserts the dead-letter record, and commits immediately (auto-commit semantics).

## Rationale

1. Dead-lettering is an operational diagnostic and fault-handling concern, not a business state mutation.
2. Background dispatch workers cannot rely on ambient transactional scopes.
3. If database connectivity fails during dead-letter persistence, the in-flight lease will expire and the stale message reclaim mechanism will re-surface the message for recovery without data loss.

## Consequences

### Positive
- Robust, decoupled error handling during background processing.
- Clean API contract that supports both standalone background logging and manual transactional dead-letter operations.

## Related ADRs

- ADR-006: Bounded Channels Dispatcher
- ADR-017: No Exactly-Once Delivery
