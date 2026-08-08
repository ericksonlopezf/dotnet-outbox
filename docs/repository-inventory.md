# Repository Inventory

## Projects and Target Frameworks

| Project | Type | Target Framework(s) |
| :--- | :--- | :--- |
| `EricksonLopez.Outbox` | Library (Core) | `net8.0;net9.0;net10.0` |
| `EricksonLopez.Outbox.EntityFrameworkCore` | Library (EF Core) | `net8.0;net9.0;net10.0` |
| `EricksonLopez.Outbox.MassTransit` | Library (Integration) | `net8.0;net9.0;net10.0` |
| `EricksonLopez.Outbox.Storage.PostgreSql` | Library (Storage) | `net8.0;net9.0;net10.0` |
| `EricksonLopez.Outbox.Storage.SqlServer` | Library (Storage) | `net8.0;net9.0;net10.0` |
| `EricksonLopez.Outbox.Storage.MySql` | Library (Storage) | `net8.0;net9.0;net10.0` |
| `EricksonLopez.Outbox.Storage.Oracle` | Library (Storage) | `net8.0;net9.0;net10.0` |
| `EricksonLopez.Outbox.Storage.Sqlite` | Library (Storage) | `net8.0;net9.0;net10.0` |
| `EricksonLopez.Outbox.Brokers.RabbitMQ` | Library (Broker) | `net8.0;net9.0;net10.0` |
| `EricksonLopez.Outbox.Brokers.Kafka` | Library (Broker) | `net8.0;net9.0;net10.0` |
| `EricksonLopez.Outbox.Brokers.AzureServiceBus` | Library (Broker) | `net8.0;net9.0;net10.0` |
| `EricksonLopez.Outbox.Brokers.AwsSqs` | Library (Broker) | `net8.0;net9.0;net10.0` |
| `EricksonLopez.Outbox.Brokers.GooglePubSub` | Library (Broker) | `net8.0;net9.0;net10.0` |
| `EricksonLopez.Outbox.Brokers.Nats` | Library (Broker) | `net8.0;net9.0;net10.0` |
| `EricksonLopez.Outbox.Brokers.RedisStreams` | Library (Broker) | `net8.0;net9.0;net10.0` |
| `EricksonLopez.Outbox.Analyzers` | Library (Roslyn) | `netstandard2.0` |
| `EricksonLopez.Outbox.SourceGenerators` | Library (Roslyn) | `netstandard2.0` |

### Test Projects

| Project | Type | Target Framework(s) |
| :--- | :--- | :--- |
| `EricksonLopez.Outbox.Tests` | Unit Tests | `net10.0` |
| `EricksonLopez.Outbox.AotTests` | AOT Smoke Tests | `net10.0` |
| `EricksonLopez.Outbox.IntegrationTests` | Integration Tests | `net10.0` |
| `EricksonLopez.Outbox.EntityFrameworkCore.Tests` | Unit Tests | `net10.0` |
| `EricksonLopez.Outbox.Storage.*.Tests` | Unit Tests (5 projects) | `net10.0` |
| `EricksonLopez.Outbox.Brokers.*.Tests` | Unit Tests (7 projects) | `net10.0` |

### Benchmark & Sample Projects

| Project | Type |
| :--- | :--- |
| `EricksonLopez.Outbox.Benchmarks` | BenchmarkDotNet benchmarks |
| `Sample.OrderService` | Sample ASP.NET Core API (with Dockerfile + docker-compose) |

## NuGet Dependencies (Central Package Management)

The repository uses Central Package Management (`Directory.Packages.props`). Key dependencies include:

### Runtime Dependencies

| Category | Package | Version |
|---|---|---|
| Framework | `Microsoft.Extensions.Hosting.Abstractions` | `10.0.10` |
| Framework | `Microsoft.Extensions.DependencyInjection.Abstractions` | `10.0.10` |
| Framework | `Microsoft.Extensions.Diagnostics.HealthChecks` | `10.0.10` |
| Framework | `Microsoft.Extensions.ObjectPool` | `10.0.10` |
| Framework | `Microsoft.Extensions.Logging` | `10.0.10` |
| ORM | `Microsoft.EntityFrameworkCore` | `8.0.13` |
| ORM | `Microsoft.EntityFrameworkCore.Relational` | `8.0.13` |
| Database | `Npgsql` | `9.0.0` |
| Database | `Microsoft.Data.SqlClient` | `5.2.1` |
| Database | `Microsoft.Data.Sqlite` | `8.0.4` |
| Database | `MySqlConnector` | `2.3.6` |
| Database | `Oracle.ManagedDataAccess.Core` | `23.4.0` |
| Broker | `RabbitMQ.Client` | `7.1.1` |
| Broker | `Confluent.Kafka` | `2.3.0` |
| Broker | `Azure.Messaging.ServiceBus` | `7.17.4` |
| Broker | `AWSSDK.SQS` | `3.7.300.73` |
| Broker | `Google.Cloud.PubSub.V1` | `3.23.0` |
| Broker | `NATS.Client.Core` | `2.5.5` |
| Broker | `StackExchange.Redis` | `2.8.0` |
| Broker | `MassTransit` | `8.2.1` |
| Roslyn | `Microsoft.CodeAnalysis.CSharp` | `4.8.0` |
| Build | `Microsoft.SourceLink.GitHub` | `8.0.0` |

### Test & Tooling Dependencies

| Category | Package | Version |
|---|---|---|
| Testing | `xunit` | `2.7.0` |
| Testing | `AwesomeAssertions` | `9.5.0` |
| Testing | `NSubstitute` | `5.1.0` |
| Testing | `Moq` | `4.20.70` |
| Testing | `AutoFixture` | `4.18.1` |
| Testing | `FsCheck.Xunit` | `3.0.0-rc3` |
| Testing | `Testcontainers` | `4.13.0` |
| Coverage | `coverlet.collector` | `6.0.1` |
| Coverage | `coverlet.msbuild` | `10.0.1` |
| Benchmarks | `BenchmarkDotNet` | `0.13.12` |

### CPM Note: Packages Pinned But Not Used in src/

The following packages are pinned in `Directory.Packages.props` but only referenced by test or sample projects (not by any `src/` library project):

- `Dapper` `2.1.35` — used in storage test projects only (integration test helpers)
- `NServiceBus` `10.2.8` — pinned for future integration (see [ROADMAP.md](../ROADMAP.md))
- `WolverineFx` `6.24.7` — pinned for future integration
- `DotNetCore.CAP` `8.2.0` — pinned for future integration
- `MediatR` `14.2.0` — pinned for future integration
- `Swashbuckle.AspNetCore` `10.2.3` — used by Sample.OrderService only

## Quality Tools

| Tool | Configuration | Integration |
|---|---|---|
| Code Coverage | `coverlet.runsettings` | Codecov (via `codecov/codecov-action@v5`) |
| Mutation Testing | `stryker-config.json`, `stryker-config-unit.json` | GitHub Actions (scheduled + manual) |
| Static Analysis | SonarCloud (`dotnet-sonarscanner`) | GitHub Actions (CI workflow) |
| Roslyn Analyzers | `EricksonLopez.Outbox.Analyzers` (OUTBOX001, OUTBOX002) | IDE + build-time |
| Code Style | `EnforceCodeStyleInBuild=true` in `Directory.Build.props` | Build-time enforcement |

## CI/CD Artifacts

*   **SDK Version:** `10.0.100` (`global.json` with `rollForward: latestFeature`)
*   **Solution File:** `EricksonLopez.Outbox.slnx` (XML-based solution format)
*   **Workflows:**
    *   `ci.yml` — CI orchestrator (calls build-test + AOT smoke test)
    *   `dotnet-build-test.yml` — Reusable build, test, SonarCloud, and Codecov workflow
    *   `aot-smoke-test.yml` — NativeAOT compilation + execution validation
    *   `mutation-testing.yml` — Stryker mutation analysis (weekly schedule + manual)
    *   `benchmarks.yml` — BenchmarkDotNet baseline capture (on tag push + manual)
    *   `weekly-benchmarks.yml` — Deep performance benchmarks (weekly schedule)
    *   `publish.yml` — NuGet packaging and publishing (on tag push + manual)
    *   `release-please.yml` — Automated release management via Conventional Commits
*   **Secrets Required:**
    *   `SNK_KEY` — Base64-encoded Strong Name key for assembly signing
    *   `CODECOV_TOKEN` — Codecov upload token
    *   `SONAR_TOKEN` — SonarCloud analysis token
    *   `GITHUB_TOKEN` — Provided automatically by GitHub Actions

## Security Artifacts

*   **Strong Naming:** `EricksonLopez.snk` signs all assemblies (decoded from `SNK_KEY` secret in CI)
*   **Trusted Publishing:** OIDC via `NuGet/login@v1` (no static API key)
*   **Sigstore Attestation:** `actions/attest-build-provenance@v2` on all `.nupkg` files
*   **NuGet Audit:** `NuGetAudit=true`, `NuGetAuditMode=all`, `NuGetAuditLevel=low`
*   **Package Validation:** `EnablePackageValidation=true` for all packable projects

## Additional Artifacts

| Artifact | Path | Purpose |
|---|---|---|
| Grafana Dashboard | `grafana/dashboards/outbox-dashboard.json` | Observability dashboard template |
| Coverlet Settings | `coverlet.runsettings` | Coverage exclusion configuration |
| Dependabot Config | `.github/dependabot.yml` | NuGet (weekly) + GitHub Actions (monthly) dependency updates |
