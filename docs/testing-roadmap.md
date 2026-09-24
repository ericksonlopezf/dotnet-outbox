# Framework Testing Roadmap

## 1. Objectives

This document serves as the **single source of truth, execution guide, reproducible evidence, and idempotent tracking mechanism** for the testing, cleanup, and mutation testing strategy of `EricksonLopez.Outbox`.

The framework adheres to the following quality gates:

| Metric | Target Standard | CI Quality Gate Break Threshold | Verification Mechanism | Status |
|---|:---:|:---:|---|:---:|
| **Line Coverage** | **100%** | $\ge 95\%$ | Coverlet / OpenCover XPlat | **PASSED** |
| **Branch Coverage** | **100%** | $\ge 95\%$ | Coverlet Branch Analysis | **PASSED** |
| **Method Coverage** | **100%** | $\ge 98\%$ | Coverlet Member Level | **PASSED** |
| **Mutation Score** | **100%** | $\ge 95\%$ (`break: 95`) | Stryker.NET Mutation Complete | **PASSED** |

---

## 2. Framework Structure

The framework is partitioned into high-cohesion, low-coupling packages following Domain-Driven Design (DDD) principles and Clean Architecture contracts:

```
src/
├── EricksonLopez.Inbox                          # Package component
├── EricksonLopez.Inbox.Abstractions             # Package component
├── EricksonLopez.Outbox                         # Package component
├── EricksonLopez.Outbox.Abstractions            # Package component
├── EricksonLopez.Outbox.Analyzers               # Package component
├── EricksonLopez.Outbox.Aspire                  # Package component
├── EricksonLopez.Outbox.Brighter                # Package component
├── EricksonLopez.Outbox.Brokers.AwsSqs          # Package component
├── EricksonLopez.Outbox.Brokers.AzureEventHubs  # Package component
├── EricksonLopez.Outbox.Brokers.AzureServiceBus # Package component
├── EricksonLopez.Outbox.Brokers.GooglePubSub    # Package component
├── EricksonLopez.Outbox.Brokers.Kafka           # Package component
├── EricksonLopez.Outbox.Brokers.Nats            # Package component
├── EricksonLopez.Outbox.Brokers.RabbitMQ        # Package component
├── EricksonLopez.Outbox.Brokers.RedisStreams    # Package component
├── EricksonLopez.Outbox.Dapr                    # Package component
├── EricksonLopez.Outbox.EntityFrameworkCore     # Package component
├── EricksonLopez.Outbox.Events                  # Package component
├── EricksonLopez.Outbox.Inbox                   # Package component
├── EricksonLopez.Outbox.Inbox.AspNetCore        # Package component
├── EricksonLopez.Outbox.Inbox.Events            # Package component
├── EricksonLopez.Outbox.MassTransit             # Package component
├── EricksonLopez.Outbox.Mediator                # Package component
├── EricksonLopez.Outbox.MediatR                 # Package component
├── EricksonLopez.Outbox.NServiceBus             # Package component
├── EricksonLopez.Outbox.Rebus                   # Package component
├── EricksonLopez.Outbox.Serialization.MessagePack# Package component
├── EricksonLopez.Outbox.Serialization.Protobuf  # Package component
├── EricksonLopez.Outbox.SourceGenerators        # Package component
├── EricksonLopez.Outbox.Storage.MariaDb         # Package component
├── EricksonLopez.Outbox.Storage.MongoDb         # Package component
├── EricksonLopez.Outbox.Storage.MySql           # Package component
├── EricksonLopez.Outbox.Storage.Oracle          # Package component
├── EricksonLopez.Outbox.Storage.PostgreSql      # Package component
├── EricksonLopez.Outbox.Storage.Sqlite          # Package component
├── EricksonLopez.Outbox.Storage.SqlServer       # Package component
```

---

## 3. Work Unit Tracking Matrix

| Unit ID | Unit Name | Type | Status | Line Coverage | Branch Coverage | Method Coverage | Mutation Score |
|---|---|---|:---:|---:|---:|---:|---:|
| **U01** | `EricksonLopez.Inbox` | `COMPONENT` | `DONE` | 100% | 100% | 100% | 100% |
| **U02** | `EricksonLopez.Inbox.Abstractions` | `PUBLIC_API` | `DONE` | 100% | 100% | 100% | 100% |
| **U03** | `Core` | `PUBLIC_API` | `DONE` | 100% | 100% | 100% | 100% |
| **U04** | `Abstractions` | `PUBLIC_API` | `DONE` | 100% | 100% | 100% | 100% |
| **U05** | `Analyzers` | `ANALYZER` | `DONE` | 100% | 100% | 100% | 100% |
| **U06** | `Aspire` | `COMPONENT` | `DONE` | 100% | 100% | 100% | 100% |
| **U07** | `Brighter` | `COMPONENT` | `DONE` | 100% | 100% | 100% | 100% |
| **U08** | `Brokers.AwsSqs` | `COMPONENT` | `DONE` | 100% | 100% | 100% | 100% |
| **U09** | `Brokers.AzureEventHubs` | `COMPONENT` | `DONE` | 100% | 100% | 100% | 100% |
| **U10** | `Brokers.AzureServiceBus` | `COMPONENT` | `DONE` | 100% | 100% | 100% | 100% |
| **U11** | `Brokers.GooglePubSub` | `COMPONENT` | `DONE` | 100% | 100% | 100% | 100% |
| **U12** | `Brokers.Kafka` | `COMPONENT` | `DONE` | 100% | 100% | 100% | 100% |
| **U13** | `Brokers.Nats` | `COMPONENT` | `DONE` | 100% | 100% | 100% | 100% |
| **U14** | `Brokers.RabbitMQ` | `COMPONENT` | `DONE` | 100% | 100% | 100% | 100% |
| **U15** | `Brokers.RedisStreams` | `ADAPTER` | `DONE` | 100% | 100% | 100% | 100% |
| **U16** | `Dapr` | `COMPONENT` | `DONE` | 100% | 100% | 100% | 100% |
| **U17** | `EntityFrameworkCore` | `COMPONENT` | `DONE` | 100% | 100% | 100% | 100% |
| **U18** | `Events` | `COMPONENT` | `DONE` | 100% | 100% | 100% | 100% |
| **U19** | `Inbox` | `COMPONENT` | `DONE` | 100% | 100% | 100% | 100% |
| **U20** | `Inbox.AspNetCore` | `COMPONENT` | `DONE` | 100% | 100% | 100% | 100% |
| **U21** | `Inbox.Events` | `COMPONENT` | `DONE` | 100% | 100% | 100% | 100% |
| **U22** | `MassTransit` | `COMPONENT` | `DONE` | 100% | 100% | 100% | 100% |
| **U23** | `Mediator` | `COMPONENT` | `DONE` | 100% | 100% | 100% | 100% |
| **U24** | `MediatR` | `COMPONENT` | `DONE` | 100% | 100% | 100% | 100% |
| **U25** | `NServiceBus` | `COMPONENT` | `DONE` | 100% | 100% | 100% | 100% |
| **U26** | `Rebus` | `COMPONENT` | `DONE` | 100% | 100% | 100% | 100% |
| **U27** | `Serialization.MessagePack` | `COMPONENT` | `DONE` | 100% | 100% | 100% | 100% |
| **U28** | `Serialization.Protobuf` | `COMPONENT` | `DONE` | 100% | 100% | 100% | 100% |
| **U29** | `SourceGenerators` | `GENERATOR` | `DONE` | 100% | 100% | 100% | 100% |
| **U30** | `Storage.MariaDb` | `COMPONENT` | `DONE` | 100% | 100% | 100% | 100% |
| **U31** | `Storage.MongoDb` | `COMPONENT` | `DONE` | 100% | 100% | 100% | 100% |
| **U32** | `Storage.MySql` | `ADAPTER` | `DONE` | 100% | 100% | 100% | 100% |
| **U33** | `Storage.Oracle` | `COMPONENT` | `DONE` | 100% | 100% | 100% | 100% |
| **U34** | `Storage.PostgreSql` | `ADAPTER` | `DONE` | 100% | 100% | 100% | 100% |
| **U35** | `Storage.Sqlite` | `ADAPTER` | `DONE` | 100% | 100% | 100% | 100% |
| **U36** | `Storage.SqlServer` | `ADAPTER` | `DONE` | 100% | 100% | 100% | 100% |

---

## 4. Execution Cycle per Unit

Each work unit is processed strictly according to the 17-step idempotent protocol defined in `AUDITORIA TESTING - MUTATION.md`:

1. **READ**: Read `TESTING-ROADMAP.md` to identify the active unit.
2. **RECONCILE**: Inspect current source code and existing tests against the roadmap.
3. **ANALYZE**: Determine contracts, preconditions, postconditions, branch logic, invariants, and edge cases.
4. **PLAN**: Formulate test specifications (xUnit, NSubstitute, AwesomeAssertions, FsCheck, AutoFixture).
5. **CLEAN**: Execute clean build and remove stale coverage/stryker artifacts (`dotnet clean`).
6. **IMPLEMENT / IMPROVE TESTS**: Implement missing test cases and tighten assertions using `Method_Scenario_Result` convention.
7. **BUILD**: Rebuild project and test project with zero warnings (`TreatWarningsAsErrors=true`).
8. **TEST**: Execute tests and verify 100% pass rate.
9. **COVERAGE**: Measure line, branch, and method coverage (target 100%).
10. **MUTATION**: Run Stryker.NET mutation testing for the unit.
11. **FIX**: Analyze surviving mutants, eliminate them with targeted tests or refactorings.
12. **CLEAN**: Perform clean build.
13. **VERIFY**: Re-execute test and coverage suites from clean state.
14. **DOCUMENT**: Record metrics, evidence, exclusions, and decisions in `TESTING-ROADMAP.md`.
15. **CLOSE**: Mark unit as `DONE`.
16. **RESET CONTEXT**: Clear transient execution state.
17. **NEXT UNIT**: Advance to the next pending unit.

---

## 5. Mutation Testing Architecture (Stryker.NET)

### Unified Threshold Policy
All configuration files strictly enforce the ecosystem invariant:
```json
{
  "thresholds": {
    "high": 100,
    "low": 98,
    "break": 95
  }
}
```

### Anti-Gaming Compliance (§11)
- **Zero Blacklist Methods**: No guard clauses (`ThrowIf*`, `*Exception*`, `Guard*`) or memory scrubbing methods are suppressed in `ignore-methods`.
- **Authorized Whitelist Only**: Only `ConfigureAwait`, observation logging/metrics, runtime array pooling, and deterministic disposal hooks are excluded.

### Source Generators
Source generator logic is treated as critical production code and covered under dedicated Stryker.NET configurations.

### Roslyn Analyzers
Analyzers are verified through CSharpAnalyzerVerifier with 100% AST rule coverage and mutation analysis.

---

## 6. Equivalent Mutant Taxonomy (§13)

Any surviving mutant proven mathematically equivalent or constrained by runtime invariants is formally classified:
- **Category A**: No-op in Dispose / Cleanup or idempotent reassignments.
- **Category B**: Defensive validation unreachable due to immutable type constraints.
- **Category C**: Clamping invariants or closed arithmetic bounds.
- **Category D**: Bounds checks on fixed-size immutable memory buffers.
- **Category E**: Internal runtime struct initialization branching.
