# ADR 014: Exclude Integration Tests from Mutation Coverage (Stryker)

Date: 2026-08-07

## Status

Accepted

## Context

During code quality validation, we established a strict mandate for 100% mutation coverage using **Stryker**. The `EricksonLopez.Outbox` ecosystem contains multiple storage packages (MySQL, PostgreSQL, SQL Server, Oracle) and messaging brokers (Kafka, Azure Service Bus, AWS SQS, Google PubSub, RabbitMQ, Nats, Redis Streams).

To ensure these packages work correctly with real infrastructure, hundreds of **Integration Tests** were designed, driven by `Testcontainers`. This allows starting and destroying Docker containers (real databases and brokers) during test execution.

The problem arose when attempting to run Stryker on these integration projects:
- Stryker generates dozens or hundreds of mutants per project.
- Each mutant triggers an isolated test execution that spins up transactions, initializes infrastructure, and often creates/destroys Docker containers.
- The execution time to achieve mutation coverage in these projects became prohibitive (over 45 minutes for the MySQL project alone), causing hangs and instability in the pipeline or the Docker host machine.

## Decision

1. **Run Stryker only on Unit Tests:** Projects testing the domain or business rules without heavy external dependencies (`EricksonLopez.Outbox.Tests`, `EricksonLopez.Outbox.EntityFrameworkCore.Tests`) will be the only ones evaluated via Mutation Testing, maintaining the 100% threshold.
2. **Exclude Integration Tests from Stryker:** `Storage` and `Brokers` projects (which depend on `Testcontainers` or live connections) will no longer be processed by Stryker. 
3. **Maintenance in CI:** Integration tests will remain an integral part of the CI/CD pipeline. They will be executed with `dotnet test` to validate behavior across components (line coverage and test success), but **not** to evaluate structural quality via code mutation.

## Consequences

### Positive
- **Speed and Reliability:** CI feedback cycles for mutation tests will be reduced to a couple of minutes, allowing agile development without punishing the Docker engine resources.
- **Reproducibility:** Any developer can run `.\run-stryker-all.ps1` locally without fear of exhausting their memory, CPU, or requiring hours of processing from Testcontainers.
- **Clear Purpose:** Concepts are properly separated: unit tests guarantee design quality (100% mutation), while integration tests guarantee correct infrastructure wiring (traditional pass/fail).

### Negative
- **Theoretical Mutation Gap:** Connector-specific components (like SQL queries without Dapper in storage repositories) will not be mathematically shielded against regression errors that Stryker could detect (e.g., mutating a `>`, or modifying a column name in a query if it was not wrapped in a mocked unit test). We will rely on the traditional assertiveness of integration tests to cover these scenarios.
