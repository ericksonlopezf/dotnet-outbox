<!-- Copyright © Erickson Lopez. MIT License. -->

# ADR-024 — MaxDegreeOfParallelism Must Be Implemented Or Removed

## Status

Accepted — Implemented

## Context

`OutboxDispatcherOptions.MaxDegreeOfParallelism` was previously documented as "ISSUE-CC3: Not yet implemented" despite being a public configuration property.

## Decision

`MaxDegreeOfParallelism` is **fully implemented** in `OutboxDispatcherBackgroundService` and `OutboxChannel`. The dispatcher spawns N concurrent consumer worker tasks (where `N = MaxDegreeOfParallelism`), each independently draining the bounded `OutboxChannel` and executing the pipeline/publishing loop. Single-reader channel optimizations are conditionally applied when `MaxDegreeOfParallelism == 1`.

## Rationale

1. A public API property with documented "has no effect" is a broken contract.
2. Users who read the documentation and set `MaxDegreeOfParallelism = 4` expect the dispatcher to process messages in parallel. They receive no parallelism.
3. The property's current default `min(ProcessorCount, 8)` means the documentation says it has no effect, yet it has a non-trivial default that suggests it should do something.
4. This is a correctness issue, not a performance issue.

## Alternatives Considered

### Alternative 1: Implement N concurrent consumer tasks
Preferred: `OutboxChannel` spawns `MaxDegreeOfParallelism` tasks at startup, each draining the channel independently. The existing `SingleReader` flag already has the correct branching.

### Alternative 2: Remove property and replace with internal constant (1)
Acceptable: document that the dispatcher is single-threaded per node; horizontal scaling is via multiple pods, not within a single dispatcher.

### Alternative 3: Keep property but mark `[Obsolete]` and always treat as 1
Not acceptable: still deceives users about the behavior.

## Rejected Alternatives

Alternative 3 is rejected. Keeping a non-functional property as deprecated is equivalent to shipping a broken feature.

## Consequences

### Positive (if implemented)
- Dispatcher can saturate CPU on message processing
- Allows higher throughput per pod without requiring more pods

### Negative (if implemented)
- Multiple concurrent `ProcessMessagesAsync` calls on the same `OutboxChannel` require verifying thread safety of all shared state (headers cache is already per-call, pipeline is immutable)
- Must set `SingleReader = false` when `MaxDegreeOfParallelism > 1`

### Positive (if removed)
- No deceptive API
- Users scale horizontally via pods (which is the correct scaling dimension for the Outbox pattern anyway)

## Ecosystem Impact

If implemented: dispatcher parallelism changes require updated benchmarks and concurrency tests.
If removed: breaking change (MAJOR version bump) — users who set this property get a compile error.

## Migration

If removed: users who explicitly set `MaxDegreeOfParallelism` will get a compile error. The migration path is to delete the property assignment (the behavior was already single-threaded).

## Related ADRs

- ADR-024 (This ADR)
