# ADR 013: Stryker.NET Mutation Coverage Exclusions

## Context
The goal of the `EricksonLopez.Outbox` project is to maintain an impeccable quality standard, aiming for 100% Code, Branch, Mutation, and Method Coverage. During the integration of **Stryker.NET** to measure the Mutation Score, we identified a subset of mutants that consistently survive due to technical limitations of the testing framework (`xUnit`), inherent .NET asynchrony, defensive design, or hardware-bound constraints. Testing these mutated edge cases is extremely fragile or outright impossible.

Specifically, we face issues with:
1. **`.ConfigureAwait(false)`**: Stryker mutates the booleans to `true`. Without a `SynchronizationContext` (xUnit lacks it by default), the asynchronous flow behaves identically, making it impossible for a unit test to detect this mutation deterministically.
2. **Compiler-Generated Code**: Certain structures such as implicit initializations or asynchronous state machines generate mutants that have no direct or actionable mapping in the source code.
3. **Impossible Exceptions & Validation Strings**: Exceptions thrown in flows where the system enters an "impossible" state (purely defensive). Furthermore, enforcing exact string matches for validation messages yields brittle tests.
4. **Time Boundaries and Jitter (Non-Deterministic Timers)**: Mutating delays (`Task.Delay`) and jitter mathematics only proves that the OS (Windows/Linux) scheduler decided to execute a thread a few milliseconds early or late. The essential behavior is validating that the system *retries*, *respects the max threshold*, and *eventually stops retrying*, rather than demanding it sleeps for exactly 1000ms.
5. **Memory Management and Security (e.g. `clearArray: true`)**: Features like array pooling include defensive cleanup logic to avoid leaving sensitive data in memory. Verifying if a buffer was successfully cleared via GC assertions inside a unit test causes massive memory spikes and brittle outcomes, providing minimal value relative to the test complexity.
6. **Telemetry & Observability (Logs and Metrics)**: Asserting whether a log states exactly `"Dispatch failed"` instead of `"dispatch failed"` adds no real value and pollutes the test suite. *Note*: However, the existence of critical telemetry counters (e.g., `_metrics.DispatchFailures.Add(1)`) is an operational contract. Omitting them alters the observable behavior for production alerting. Therefore, while exact tags and casing are excluded from unit tests, the presence of the metric should be validated through integration tests.
7. **Dependency Injection Setup**: Validating DI containers or `IOptions` initializations through mutation testing yields minimal value. These setups are best validated holistically by resolving the service from the Host in an integration test.
8. **Test Helpers (Test Doubles / Mocks)**: Running mutation testing on testing infrastructure (e.g., `Testing/**/*.cs`) is counterproductive, as it breaks the tests themselves by mutating the very assertions and fake dependencies designed to validate the production code.

## Decision
We have decided to **systematically ignore** these scenarios through Stryker's native configuration (`stryker-config.json`) and specific source code exclusions (`// Stryker disable`), rather than attempting to force their coverage with low-value artificial tests.

The approved exclusions include:
- `ConfigureAwait(false)`.
- **String mutations** on logs, telemetry tags, and exception validation messages.
- **Math and equality mutations** on Jitter and randomized backoff limits.
- **Array Pooling cleanup logic** and hardware limits (e.g., OOM edge cases).
- **DI and Setup modules** (should rely on integration tests).
- **Test Helpers** (excluded via `stryker-config.json` glob patterns).

## Consequences

### Positive
- **High-Value Tests**: The test suite will not be cluttered with artificial and fragile "brittle tests" designed exclusively to appease the mutation tool.
- **Focus on Business Logic**: A massive amount of "false positives" in the Stryker report is eliminated, allowing developers to focus on surviving mutants that represent real logical errors.
- **Execution Times**: Fewer compiled mutants translate to drastically faster evaluation times.

### Negative
- Potential loss of real coverage if we mistakenly configure method ignoring as a generic wildcard that hides code with true business responsibility (e.g., exceptions that *should* be handled and tested). This is mitigated by restricting wildcards in `stryker-config.json` and ensuring critical paths (like telemetry emissions) are covered by higher-level integration tests.
