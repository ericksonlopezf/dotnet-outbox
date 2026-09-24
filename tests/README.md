<!-- Copyright © Erickson Lopez. MIT License. -->

# EricksonLopez.Outbox — Testing Guide & Architecture

This repository adheres to high-rigor QA standards for high-throughput, Native AOT-compatible transactional outbox and inbox patterns in .NET.

---

## 1. Test Project Structure

| Project | Type | Purpose | External Dependencies |
| :--- | :--- | :--- | :--- |
| `EricksonLopez.Outbox.Tests` | Unit & Fuzz | Core outbox mechanics, adaptive poller, channels, partitioning, property-based tests. | None (In-memory) |
| `EricksonLopez.Outbox.Analyzers.Tests` | Analyzer | Roslyn analyzer and code-fix verification (`CSharpAnalyzerTest`). | None |
| `EricksonLopez.Outbox.Brokers.*.Tests` | Unit | Publisher contracts, routing keys, headers serialization, retry policies. | NSubstitute |
| `EricksonLopez.Outbox.Storage.Sqlite.Tests` | Component | SQLite repository, DLQ, and idempotency store with isolated in-memory DBs. | In-memory SQLite |
| `EricksonLopez.Outbox.Storage.PostgreSql.Tests` | Integration | PostgreSQL partitioning, UNNEST batch inserts, and advisory lock dispatching. | Docker / Testcontainers |
| `EricksonLopez.Outbox.Storage.SqlServer.Tests` | Integration | SQL Server table hints (`READPAST`, `UPDLOCK`) and `SqlBulkCopy`. | Docker / Testcontainers |
| `EricksonLopez.Outbox.IntegrationTests` | Chaos / E2E | Network drops (Toxiproxy), failover resilience, latency simulation. | Docker / Testcontainers |

---

## 2. Running Tests Locally

### Fast Unit Test Run (No Docker required)
Runs all unit, analyzer, serializer, and broker tests in < 5 seconds:
```bash
dotnet test --filter "Category!=Integration" --nologo
```

### Storage Unit Tests (SQLite isolated)
```bash
dotnet test tests/EricksonLopez.Outbox.Storage.Sqlite.Tests --nologo
```

### Full Solution Test Run (Requires Docker for Testcontainers)
```bash
dotnet test --nologo
```

---

## 3. Test Fixtures & Utilities

- **`TestDispatcherHarness`**: Encapsulates DI registration (`IServiceProvider`), options binding, channels, and `AdaptivePoller` creation for concise Arrange blocks.
- **`OutboxMessageTestDataBuilder`**: Provides fluent, safe instantiation for `OutboxMessage` records, avoiding brittle multi-argument constructors.
- **`FakeBrokerPublisher` / `InMemoryOutboxStore`**: Public testing package (`EricksonLopez.Outbox.Testing`) providing in-memory doubles for consumer unit tests.

---

## 4. Mutation Testing with Stryker.NET

To verify mutation score and catch surviving mutants:

```bash
# Run unit tests mutation analysis
dotnet stryker -c stryker-config-unit.json

# Run complete mutation suite
dotnet stryker -c stryker-config.json
```

### Thresholds & Exclusion Policy
- **Thresholds (`break=95`, `high=100`)**: We enforce a strict quality gate aiming for mutation scores ≥97% across the core engine. Any build with a mutation score below 95% fails CI.
- **Testing Package Exclusions (`!**/Testing/**/*.cs`)**: Consumer-facing test infrastructure (`EricksonLopez.Outbox.Testing`) is excluded from mutation analysis because mutating assertions and fake test doubles introduces false positives without validating production logic.
- **String & Telemetry Exclusions**: Non-behavioral string mutations (e.g. logging formats, exception message wording, Roslyn diagnostic UI descriptors) are ignored to avoid fragile tests.
- **Architecture Decision Record**: See [ADR 013: Stryker.NET Mutation Coverage Exclusions](../docs/adr/013-stryker-mutation-exclusions.md) for full technical rationale and approved suppression rules.

---

## 5. Testing Internals Policy

Our test suite adheres to strict guidelines regarding the inspection and manipulation of non-public APIs:

1. **Preference for `InternalsVisibleTo`**:
   - Internal types, methods, and lifecycle hooks designed for library collaboration (e.g., `WaitForRunningAsync`, `BuildMetadata`) are exposed to `EricksonLopez.Outbox.Tests` via `[assembly: InternalsVisibleTo]`.
   - Prefer promoting private helper methods to `internal` or `internal static` if testing them directly improves test isolation without exposing internal state to public consumers.

2. **White-Box Testing & Reflection (`ReflectionTestHelper`)**:
   - Accessing `private` fields or methods via reflection is strictly restricted to scenarios where external simulation is impossible or non-deterministic (e.g., forcing timestamp elapsed thresholds on `_lastMetricTick` to achieve 100% branch coverage without relying on system uptime).
   - All reflection calls **must** use `ReflectionTestHelper` (e.g., `GetFieldValue`, `SetFieldValue`, `GetMethodOrThrow`) to guarantee informative exception messages if fields or methods are refactored.
   - Every reflection call **must** include an inline code comment documenting the technical rationale (e.g., `// White-box test rationale: ...`).

3. **Deterministic Synchronization**:
   - Never use arbitrary `Task.Delay` or polling loops in assertions. Use `TimeProvider` / `FakeTimeProvider` or deterministic signals (`TaskCompletionSource`, `WaitForRunningAsync`, mock callbacks) for all asynchronous synchronization.

