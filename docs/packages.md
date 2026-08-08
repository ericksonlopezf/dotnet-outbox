# NuGet Packages and Versioning

The `EricksonLopez.Outbox` library ecosystem is distributed as a suite of cohesive
NuGet packages. Consumers pick only the packages they need.

---

## 1. Produced Packages

The following packages are produced by the monorepo. All are packed and published
by the `publish.yml` workflow. Packability is implicit for `src/` projects — only
test/benchmark projects set `<IsPackable>false</IsPackable>`.

| Package ID | Source Project | Target Frameworks | Description |
|---|---|---|---|
| `EricksonLopez.Outbox` | `src/EricksonLopez.Outbox/` | `net8.0`, `net9.0`, `net10.0` | Core abstractions, dispatcher background service, retry pipeline, idempotency middleware |
| `EricksonLopez.Outbox.EntityFrameworkCore` | `src/EricksonLopez.Outbox.EntityFrameworkCore/` | `net8.0`, `net9.0`, `net10.0` | EF Core `DbContext` integration, model builder extensions, and EF-backed repositories |
| `EricksonLopez.Outbox.MassTransit` | `src/EricksonLopez.Outbox.MassTransit/` | `net8.0`, `net9.0`, `net10.0` | MassTransit `IBrokerPublisher` adapter and `InboxIdempotencyFilter` |
| `EricksonLopez.Outbox.Storage.PostgreSql` | `src/EricksonLopez.Outbox.Storage.PostgreSql/` | `net8.0`, `net9.0`, `net10.0` | Raw ADO.NET `IOutboxRepository` for PostgreSQL (Npgsql) |
| `EricksonLopez.Outbox.Storage.SqlServer` | `src/EricksonLopez.Outbox.Storage.SqlServer/` | `net8.0`, `net9.0`, `net10.0` | Raw ADO.NET `IOutboxRepository` for SQL Server (Microsoft.Data.SqlClient) |
| `EricksonLopez.Outbox.Storage.MySql` | `src/EricksonLopez.Outbox.Storage.MySql/` | `net8.0`, `net9.0`, `net10.0` | Raw ADO.NET `IOutboxRepository` for MySQL (MySqlConnector) |
| `EricksonLopez.Outbox.Storage.Oracle` | `src/EricksonLopez.Outbox.Storage.Oracle/` | `net8.0`, `net9.0`, `net10.0` | Raw ADO.NET `IOutboxRepository` for Oracle (Oracle.ManagedDataAccess.Core) |
| `EricksonLopez.Outbox.Storage.Sqlite` | `src/EricksonLopez.Outbox.Storage.Sqlite/` | `net8.0`, `net9.0`, `net10.0` | Raw ADO.NET `IOutboxRepository` for SQLite (Microsoft.Data.Sqlite) |
| `EricksonLopez.Outbox.Brokers.RabbitMQ` | `src/EricksonLopez.Outbox.Brokers.RabbitMQ/` | `net8.0`, `net9.0`, `net10.0` | `IBrokerPublisher` for RabbitMQ |
| `EricksonLopez.Outbox.Brokers.Kafka` | `src/EricksonLopez.Outbox.Brokers.Kafka/` | `net8.0`, `net9.0`, `net10.0` | `IBrokerPublisher` for Apache Kafka |
| `EricksonLopez.Outbox.Brokers.AzureServiceBus` | `src/EricksonLopez.Outbox.Brokers.AzureServiceBus/` | `net8.0`, `net9.0`, `net10.0` | `IBrokerPublisher` for Azure Service Bus |
| `EricksonLopez.Outbox.Brokers.AwsSqs` | `src/EricksonLopez.Outbox.Brokers.AwsSqs/` | `net8.0`, `net9.0`, `net10.0` | `IBrokerPublisher` for AWS SQS |
| `EricksonLopez.Outbox.Brokers.GooglePubSub` | `src/EricksonLopez.Outbox.Brokers.GooglePubSub/` | `net8.0`, `net9.0`, `net10.0` | `IBrokerPublisher` for Google Cloud Pub/Sub |
| `EricksonLopez.Outbox.Brokers.Nats` | `src/EricksonLopez.Outbox.Brokers.Nats/` | `net8.0`, `net9.0`, `net10.0` | `IBrokerPublisher` for NATS |
| `EricksonLopez.Outbox.Brokers.RedisStreams` | `src/EricksonLopez.Outbox.Brokers.RedisStreams/` | `net8.0`, `net9.0`, `net10.0` | `IBrokerPublisher` for Redis Streams |
| `EricksonLopez.Outbox.Analyzers` | `src/EricksonLopez.Outbox.Analyzers/` | `netstandard2.0` | Roslyn analyzers (OUTBOX001, OUTBOX002) — dev-time only |
| `EricksonLopez.Outbox.SourceGenerators` | `src/EricksonLopez.Outbox.SourceGenerators/` | `netstandard2.0` | Incremental source generator (`OutboxTypeMappingGenerator`) |

---

## 2. Central Package Management (CPM)

All version pins are centralized in `Directory.Packages.props`
(`<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>`).
Individual `.csproj` files reference packages **without versions** — the version
is always resolved from `Directory.Packages.props`.

### Key Pinned Versions

| Category | Package | Version |
|---|---|---|
| Framework | `Microsoft.Extensions.Hosting.Abstractions` | `10.0.10` |
| Framework | `Microsoft.Extensions.DependencyInjection.Abstractions` | `10.0.10` |
| Framework | `Microsoft.Extensions.Logging` | `10.0.10` |
| ORM | `Microsoft.EntityFrameworkCore` | `8.0.13` |
| ORM | `Microsoft.EntityFrameworkCore.Relational` | `8.0.13` |
| Database | `Npgsql` | `9.0.0` |
| Database | `Microsoft.Data.SqlClient` | `5.2.1` |
| Database | `Microsoft.Data.Sqlite` | `8.0.4` |
| Database | `MySqlConnector` | `2.3.6` |
| Database | `Oracle.ManagedDataAccess.Core` | `23.4.0` |
| Broker | `MassTransit` | `8.2.1` |
| Broker | `RabbitMQ.Client` | `7.1.1` |
| Broker | `Confluent.Kafka` | `2.3.0` |
| Broker | `Azure.Messaging.ServiceBus` | `7.17.4` |
| Broker | `AWSSDK.SQS` | `3.7.300.73` |
| Broker | `Google.Cloud.PubSub.V1` | `3.23.0` |
| Broker | `NATS.Client.Core` | `2.5.5` |
| Broker | `StackExchange.Redis` | `2.8.0` |
| Roslyn | `Microsoft.CodeAnalysis.CSharp` | `4.8.0` |
| Build | `Microsoft.SourceLink.GitHub` | `8.0.0` |
| Testing | `AwesomeAssertions` | `9.5.0` |
| Testing | `xunit` | `2.7.0` |
| Testing | `Testcontainers` | `4.13.0` |
| Benchmarks | `BenchmarkDotNet` | `0.13.12` |

---

## 3. Semantic Versioning

The project adheres to **Semantic Versioning 2.0** (SemVer).

- The base version is managed in `Directory.Build.props` via `<VersionPrefix>` (current: `1.0.0`).
- A git tag `vX.Y.Z` triggers the publish workflow.
- Pre-releases are detected automatically: if the resolved version string contains
  a hyphen (e.g., `1.0.0-preview.1`), the GitHub Release is marked as pre-release
  and the NuGet package is published with the pre-release suffix.

The **Release Please** workflow (`release-please.yml`) manages version bumping automatically
based on **Conventional Commits** (`feat:`, `fix:`, `feat!:` for breaking changes, etc.).
It reads and writes `.release-please-manifest.json` and `.release-please-config.json`.

> [!IMPORTANT]
> `PackageValidationBaselineVersion` in `Directory.Build.props` is currently **commented out**.
> No baseline version exists until the first NuGet publish of v1.0.0.
>
> **How to activate** (immediately after the first NuGet publish):
> 1. Confirm the package is live: `https://www.nuget.org/packages/EricksonLopez.Outbox/1.0.0`
> 2. Uncomment the property in `Directory.Build.props` and set it to the published version:
>    `<PackageValidationBaselineVersion>1.0.0</PackageValidationBaselineVersion>`
> 3. Commit and push. From that point, every build automatically validates backwards-compatibility
>    via `Microsoft.DotNet.ApiCompat`, failing on any public API surface regression.
>
> See the detailed step-by-step guide inside `Directory.Build.props` (lines 57–81) for full context.

---

## 4. Supply Chain Security

| Mechanism | Detail |
|---|---|
| **Strong Name Signing** | All assemblies are signed with `EricksonLopez.snk` when the `SNK_KEY` CI secret is set. The `.snk` file is not stored in the repository — it is base64-decoded from the secret at build time. |
| **OIDC Trusted Publishing** | The `publish.yml` workflow uses `NuGet/login@v1` with OIDC (no static API key). The `NUGET_API_KEY` secret does **not** exist in this repository. |
| **Sigstore Attestation** | Every `.nupkg` receives a Sigstore provenance attestation via `actions/attest-build-provenance@v2`. |
| **Package Validation** | `<EnablePackageValidation>true</EnablePackageValidation>` in `Directory.Build.props` catches breaking API surface changes between package versions. |
| **NuGet Audit** | `NuGetAudit=true` with `NuGetAuditMode=all` and `NuGetAuditLevel=low` — any CVE in any dependency (including transitive) fails the build. |

---

## 5. Symbol Packages

Symbol packages (`.snupkg`) are produced alongside `.nupkg` for all packable projects:

```xml
<IncludeSymbols>true</IncludeSymbols>
<SymbolPackageFormat>snupkg</SymbolPackageFormat>
```

This enables step-through debugging directly from consumer projects via the
NuGet.org symbol server, without requiring the consumer to reference the source repository.
