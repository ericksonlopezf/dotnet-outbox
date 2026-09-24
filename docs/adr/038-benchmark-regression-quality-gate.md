<!-- Copyright © Erickson Lopez. MIT License. -->

# ADR-038: Benchmark Regression Quality Gate in CI/CD

## Status
Accepted — September 2026

## Date
2026-09-08

## Context

`EricksonLopez.Outbox` is designed as a zero-allocation, ultra-high-throughput transactional outbox engine for cloud-native .NET applications. Performance guarantees (e.g., 0 B allocated on publishing hot paths, sub-microsecond serialization, lock-free channel polling) are central to the library's value proposition.

Historically, performance testing was executed ad-hoc on developer workstations using `benchmarks/EricksonLopez.Outbox.Benchmarks`. However, without an automated gate in the continuous integration pipeline:
1. Micro-allocations could accidentally be introduced in pull requests (e.g., unintended closure captures, boxing of `readonly record struct` instances, or LINQ usage).
2. Latency regressions could slip into releases undetected.
3. Reviewing benchmark logs manually on every PR is error-prone and time-consuming.

## Decision

Implement an automated **Benchmark Regression Gate** in GitHub Actions (`.github/workflows/benchmark-regression-gate.yml`) evaluated via a PowerShell script (`scripts/verify-benchmark-gate.ps1`):

1. **Trigger Condition**: Automatically runs on all pull requests targeting `main` or `develop` that touch `src/**` or `benchmarks/**`, and on manual workflow dispatch.
2. **Benchmark Execution**: Runs BenchmarkDotNet against `EricksonLopez.Outbox.Benchmarks.csproj` under `net10.0` Release configuration with JSON export and memory diagnosis enabled.
3. **Automated Evaluation Criteria**:
   - **Heap Invariant Gate**: Strictly enforces zero-allocation on hot path benchmarks (`0 B allocated`). Any non-zero heap allocation fails the CI build.
   - **Latency Threshold Gate**: Compares PR execution results against the stored baseline (`benchmarks/results/baseline.json`). Any mean latency regression exceeding 5% fails the CI build (configurable via workflow dispatch).
4. **Artifact Preservation**: PR benchmark reports and JSON exports are archived as GitHub Actions artifacts for 30 days.

## Rationale

- **Deterministic Quality Gate**: Moving benchmark evaluation to CI transforms performance SLAs into hard test assertions rather than aspirational documentation.
- **Fail-Fast Feedback**: Developers receive immediate automated feedback if an architectural modification inadvertently introduces GC pressure or latency degradation.
- **Reproducibility**: Baseline JSON formats allow structured tracking and programmatic comparisons without relying on human interpretation of raw console tables.

## Consequences

### Positive
- Prevents accidental regressions in zero-allocation hot paths.
- Enforces performance accountability on external and internal pull requests.
- Automates performance artifact preservation across CI runs.

### Negative
- CI pull request execution time increases by ~10-15 minutes when benchmark jobs are triggered.
- Shared CI runner hardware noise can occasionally introduce latency jitter; the 5% threshold and short warmups mitigate this while keeping micro-allocation detection 100% deterministic.

## Related ADRs

- [ADR-002](002-zero-allocation-models.md) — Zero-Allocation Models
- [ADR-006](006-bounded-channels-dispatcher.md) — Bounded Channels Dispatcher
- [ADR-037](037-outboxmessagebuilder-sealed-class-rationale.md) — OutboxMessageBuilder Allocation Analysis
