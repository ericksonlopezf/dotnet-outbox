<!-- Copyright © Erickson Lopez. MIT License. -->

# ADR-030 — Osherove Test Naming Standard and IDE1006 Suppression

## Status

Accepted

## Context

A high-performance enterprise messaging library requires testing suites that not only verify functional invariants and mutation resilience, but also serve as **living executable specifications**. In continuous integration (CI) environments (e.g. `dotnet test`, Azure DevOps Pipelines, GitHub Actions, Trx test loggers), failed assertions must immediately convey:
1. Which unit of work or method failed.
2. Under what specific state or input scenario it was executed.
3. What precise outcome or behavior was expected.

Standard C# naming conventions enforced by the Roslyn analyzer `IDE1006` (`Naming Styles`) mandate strict PascalCase without underscores for methods. While PascalCase is the gold standard for public production APIs (`EricksonLopez.Outbox`), applying it blindly to test methods produces concatenated, unreadable method names (e.g. `StoreAsyncWithAsyncRepositoryShouldAwaitAndRecordMetrics` vs `StoreAsync_WithAsyncRepository_ShouldAwaitAndRecordMetrics`).

## Decision

1. **Standardize on Roy Osherove's Naming Pattern**: All test methods across the entire test suite (`tests/**/*.cs`) must follow the Osherove convention:
   $$\text{UnitOfWork\_StateUnderTest\_ExpectedBehavior}$$
   or its equivalent:
   $$\text{Method\_Scenario\_Result}$$

   Examples:
   - `ExecuteAsync_WhenDisabled_ExitsImmediately`
   - `StoreAsync_WhenTransactionIsNull_ThrowsArgumentNullException`
   - `StartPollingAsync_WhenReclaimIntervalElapsed_ReclaimsStaleMessages`
   - `FetchPendingAsync_WhenQueueEmpty_ReturnsEmptyList`

2. **Locally Suppress `IDE1006` in Test Projects**:
   Suppress Roslyn rule `IDE1006` for all test projects via `.editorconfig` and project configuration (`Directory.Build.props`). Test methods are specifications, not public API contracts consumed by external assemblies.

## Rationale

1. **Tests as Living Documentation**: Underscores in test method names act as semantic visual delimiters separating the three primary dimensions of a test specification (Unit, Scenario, Expectation).
2. **Instant CI/CD Triage**: When a test fails in a cloud build log or dashboard, developers can diagnose the root cause and business scenario in under 5 seconds without having to open the test source code.
3. **Clean Architecture & SOLID Alignment**: Differentiates production API rules (which remain strictly compliant with Microsoft Framework Design Guidelines and `TreatWarningsAsErrors=true`) from test suites designed for diagnostic clarity.

## Consequences

### Positive
- **Drastically Improved Test Readability**: Consistent, self-documenting test names across all 26 test projects.
- **Zero Warnings Under Strict Compilation**: `IDE1006` suppression in test contexts ensures `TreatWarningsAsErrors=true` passes cleanly without sacrificing readable naming.
- **Developer Consistency**: Enforces a single, predictable standard for all contributors and automated code reviews.

### Negative
- Developers must remember to maintain the 3-part underscore convention consistently when adding new tests.

## Related ADRs

- ADR-013: Stryker.NET Mutation Coverage Exclusions
- ADR-014: Stryker Exclude Integration Tests
