<!-- Copyright © Erickson Lopez. MIT License. -->

# Contributing to EricksonLopez.Outbox

First off, thank you for considering contributing to `EricksonLopez.Outbox`! It's people like you that make this library a world-class standard for .NET.

## Code of Conduct

By participating in this project, you agree to abide by our [Code of Conduct](CODE_OF_CONDUCT.md).

## Local Development Setup

### Prerequisites
- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (pinned in `global.json`: `10.0.100`)
- [Docker Desktop](https://www.docker.com/products/docker-desktop/) or Docker Engine (required for integration tests via Testcontainers)
- [Visual Studio 2022](https://visualstudio.microsoft.com/) / Rider / VS Code

### Running Infrastructure

The integration test suites use **Testcontainers** — they automatically spin up PostgreSQL, SQL Server, RabbitMQ, and other dependencies as Docker containers. You only need Docker running locally; no manual `docker-compose up` is required for tests.

To run the **sample application** (`Sample.OrderService`), use the provided Docker Compose file:

```bash
docker-compose -f samples/Sample.OrderService/docker-compose.yml up -d
```

### Building the Project

Build the solution using Release configuration:

```bash
dotnet build EricksonLopez.Outbox.slnx -c Release
```

> **Note:** `TreatWarningsAsErrors=true` and `WarningLevel=5` are configured in `Directory.Build.props`. NativeAOT compatibility flags (`IsAotCompatible`, `IsTrimmable`) are also set automatically for library projects — you do not need to pass them manually.

## Running Tests

### Unit & Integration Tests

We use `xUnit` along with `AwesomeAssertions`. Run the full test suite:

```bash
dotnet test EricksonLopez.Outbox.slnx -c Release
```

### Mutation Testing

Before opening a Pull Request, ensure you haven't broken the mutation score. Install the Stryker CLI and run it with the targeted configuration:

```bash
dotnet tool install -g dotnet-stryker

# Run mutation testing against the core package
dotnet stryker -f stryker-config.json

# Or run against a specific sub-package configuration (e.g. Postgres storage)
dotnet stryker -f stryker-postgresql-config.json
```

**Quality Gate Thresholds** (enforced per package and consolidated):
- **Target (High):** 100%
- **Warning (Low):** 98%
- **Build Break:** 95% — CI and pre-publish verification will reject any build dropping below 95%.

## Benchmarks & Zero-Allocation Rule

This library guarantees **Zero Allocations** on the hot-path (Gen 0/1/2 = 0). If you modify anything inside `EricksonLopez.Outbox` or the Serializers, you **must** run the benchmark suite locally:

```bash
dotnet run -c Release --project benchmarks/EricksonLopez.Outbox.Benchmarks -- --filter *
```

If your PR introduces an allocation (e.g., you accidentally used a `class` where a `struct` was needed, or captured a closure in a lambda), the CI pipeline will automatically reject it.

## Pull Request Guidelines

1. **Create an Issue**: Before starting massive work, please create an Issue or a Discussion to propose your changes.
2. **Branch Naming**: Use `feature/issue-id-description` or `fix/issue-id-description`.
3. **Commit Messages**: Follow [Conventional Commits](https://www.conventionalcommits.org/en/v1.0.0/) (e.g., `feat:`, `fix:`, `feat!:`). This is required by Release Please for automated version bumping.
4. **Documentation**: If you change the API, update `docs/api-reference.md` and the XML docstrings.
5. **ADR Updates**: If you make an architectural change, propose a new Architecture Decision Record (ADR) in `docs/adr/`.
6. **PR Checklist**: Review the [Pull Request Template](.github/PULL_REQUEST_TEMPLATE.md) before submitting.

Once you submit your PR, the GitHub Actions pipeline will validate your code against SonarCloud, Codecov, Stryker mutation gate, and NativeAOT compatibility. A maintainer will review your code shortly after.

