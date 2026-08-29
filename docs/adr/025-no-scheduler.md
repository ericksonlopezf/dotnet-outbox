<!-- Copyright © Erickson Lopez. MIT License. -->

# ADR-025 — Outbox Does Not Become A Scheduler

## Status

Accepted

## Context

roadmap.md previously listed generic scheduling capabilities, including delayed message delivery, timed job execution, and cron-style scheduling. The question is whether the Outbox's `available_at` column (which enables future-dated availability) constitutes a scheduler.

## Decision

`EricksonLopez.Outbox` does not implement generic scheduling. The `available_at` column exists exclusively to support retry backoff (delay before a failed message is re-tried). It is not a general-purpose scheduler. No cron expressions, no recurring jobs, no job definitions, no task management.

## Rationale

1. Scheduling is a fundamentally different problem from transactional message persistence. A scheduler needs cron semantics, job identity, recurrence, cancellation, and distributed locking.
2. The `available_at` column is a natural consequence of retry backoff — not a scheduling feature. Setting `available_at = NOW() + backoff` is not scheduling; it is visibility timeout management.
3. Existing schedulers (Quartz.NET, Hangfire, NCronJob) already solve this problem and are mature.
4. Adding scheduling would require storing job definitions, managing recurrence state, and implementing distributed coordination — all far outside the Outbox's scope.
5. The Dual Write Problem does not involve scheduling; it involves atomicity between two stores.

## Alternatives Considered

### Alternative 1: Expose `available_at` as a "delayed send" API
Partially considered: allowing callers to set `available_at` at write time is acceptable as a side effect of the model — but the library must not market this as a "scheduling" feature. It is simply "set the earliest time at which this message is eligible for dispatch."

### Alternative 2: Add cron-style recurring messages
Rejected: requires storing schedules, managing recurrence state, and ensuring at-most-one-execution semantics — all of which are out of scope.

## Rejected Alternatives

Alternative 2 is permanently rejected. Alternative 1 is acceptable only if documented as "delayed availability" (not "scheduling").

## Consequences

### Positive
- Outbox remains focused
- No dependency on any scheduling library
- No recurrence state management

### Negative
- Users who want deferred message delivery must use `OutboxMessageBuilder<T>.WithDeliverAt(DateTimeOffset)` or
  `OutboxMessageBuilder<T>.WithDelay(TimeSpan)` to set the `deliver_at` column at write time.
- No built-in support for cron-style recurring events (users must implement this in the Application layer)

## Ecosystem Impact

None. Users who need scheduling combine Outbox with Hangfire, Quartz.NET, or NCronJob where the job enqueues an outbox message.

## Migration

No migration required. This is a non-feature.

## Related ADRs

- ADR-019 (No Saga Orchestration)
- ADR-016 (Outbox Is Not An Event Bus)
