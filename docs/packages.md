<!-- Copyright © Erickson Lopez. MIT License. -->

# NuGet Packages and Versioning

The `EricksonLopez.Outbox` library ecosystem is distributed as a suite of cohesive
NuGet packages. Consumers pick only the packages they need.

---

## 1. Produced Packages

The following 36 packages are produced by the monorepo. All are packed and published
by the `publish.yml` workflow. Packability is implicit for `src/` projects — only
test/benchmark projects set `<IsPackable>false</IsPackable>`.

| Package ID | Source Project | Target Frameworks | Description |
|---|---|---|---|
| `EricksonLopez.Outbox` | `src/EricksonLopez.Outbox/` | `net8.0`, `net9.0`, `net10.0` | Core engine, dispatcher background service, retry pipeline, and testing harness |
| `EricksonLopez.Outbox.Abstractions` | `src/EricksonLopez.Outbox.Abstractions/` | `net8.0`, `net9.0`, `net10.0` | Foundational client contracts (`IOutbox`, `IOutboxTransactionContext`, `OutboxMessageMetadata`) |
| `EricksonLopez.Inbox` | `src/EricksonLopez.Inbox/` | `net8.0`, `net9.0`, `net10.0` | Standalone consumer idempotency and message deduplication engine |
| `EricksonLopez.Inbox.Abstractions` | `src/EricksonLopez.Inbox.Abstractions/` | `net8.0`, `net9.0`, `net10.0` | Foundational contracts for consumer idempotency and inbox deduplication |
| `EricksonLopez.Outbox.Events` | `src/EricksonLopez.Outbox.Events/` | `net8.0`, `net9.0`, `net10.0` | Integration with `EricksonLopez.Events` (`OutboxEventPublisher`) |
| `EricksonLopez.Outbox.Inbox.Events` | `src/EricksonLopez.Outbox.Inbox.Events/` | `net8.0`, `net9.0`, `net10.0` | Idempotent event handler pipeline integration (`IdempotentEventHandler<TEvent>`) |
| `EricksonLopez.Outbox.Inbox` | `src/EricksonLopez.Outbox.Inbox/` | `net8.0`, `net9.0`, `net10.0` | Outbox-to-Inbox deduplication bridge |
| `EricksonLopez.Outbox.Inbox.AspNetCore` | `src/EricksonLopez.Outbox.Inbox.AspNetCore/` | `net8.0`, `net9.0`, `net10.0` | ASP.NET Core endpoint filter for HTTP `Idempotency-Key` headers |
| `EricksonLopez.Outbox.EntityFrameworkCore` | `src/EricksonLopez.Outbox.EntityFrameworkCore/` | `net8.0`, `net9.0`, `net10.0` | EF Core `DbContext` integration, model builder extensions, and EF-backed repositories |
| `EricksonLopez.Outbox.MassTransit` | `src/EricksonLopez.Outbox.MassTransit/` | `net8.0`, `net9.0`, `net10.0` | MassTransit `IBrokerPublisher` adapter and `InboxIdempotencyFilter` |
| `EricksonLopez.Outbox.Mediator` | `src/EricksonLopez.Outbox.Mediator/` | `net8.0`, `net9.0`, `net10.0` | High-performance NativeAOT source-generated mediator adapter |
| `EricksonLopez.Outbox.MediatR` | `src/EricksonLopez.Outbox.MediatR/` | `net8.0`, `net9.0`, `net10.0` | Legacy MediatR adapter (deprecated in favor of Mediator, see ADR-036) |
| `EricksonLopez.Outbox.NServiceBus` | `src/EricksonLopez.Outbox.NServiceBus/` | `net8.0`, `net9.0`, `net10.0` | NServiceBus outgoing pipeline behavior and feature integration |
| `EricksonLopez.Outbox.Rebus` | `src/EricksonLopez.Outbox.Rebus/` | `net8.0`, `net9.0`, `net10.0` | Rebus outgoing pipeline step and decorator integration |
| `EricksonLopez.Outbox.Brighter` | `src/EricksonLopez.Outbox.Brighter/` | `net8.0`, `net9.0`, `net10.0` | Paramore.Brighter message producer adapter |
| `EricksonLopez.Outbox.Dapr` | `src/EricksonLopez.Outbox.Dapr/` | `net8.0`, `net9.0`, `net10.0` | Dapr Pub/Sub Cloud-Native broker adapter |
| `EricksonLopez.Outbox.Serialization.Protobuf` | `src/EricksonLopez.Outbox.Serialization.Protobuf/` | `net8.0`, `net9.0`, `net10.0` | Ultra-fast binary serializer using protobuf-net |
| `EricksonLopez.Outbox.Serialization.MessagePack` | `src/EricksonLopez.Outbox.Serialization.MessagePack/` | `net8.0`, `net9.0`, `net10.0` | Binary serializer using MessagePack-CSharp with optional LZ4 |
| `EricksonLopez.Outbox.Storage.PostgreSql` | `src/EricksonLopez.Outbox.Storage.PostgreSql/` | `net8.0`, `net9.0`, `net10.0` | Raw ADO.NET `IOutboxRepository` for PostgreSQL (Npgsql) |
| `EricksonLopez.Outbox.Storage.SqlServer` | `src/EricksonLopez.Outbox.Storage.SqlServer/` | `net8.0`, `net9.0`, `net10.0` | Raw ADO.NET `IOutboxRepository` for SQL Server (Microsoft.Data.SqlClient) |
| `EricksonLopez.Outbox.Storage.MySql` | `src/EricksonLopez.Outbox.Storage.MySql/` | `net8.0`, `net9.0`, `net10.0` | Raw ADO.NET `IOutboxRepository` for MySQL (MySqlConnector) |
| `EricksonLopez.Outbox.Storage.MariaDb` | `src/EricksonLopez.Outbox.Storage.MariaDb/` | `net8.0`, `net9.0`, `net10.0` | Raw ADO.NET `IOutboxRepository` for MariaDB (MySqlConnector) |
| `EricksonLopez.Outbox.Storage.Oracle` | `src/EricksonLopez.Outbox.Storage.Oracle/` | `net8.0`, `net9.0`, `net10.0` | Raw ADO.NET `IOutboxRepository` for Oracle (Oracle.ManagedDataAccess.Core) |
| `EricksonLopez.Outbox.Storage.Sqlite` | `src/EricksonLopez.Outbox.Storage.Sqlite/` | `net8.0`, `net9.0`, `net10.0` | Raw ADO.NET `IOutboxRepository` for SQLite (Microsoft.Data.Sqlite) |
| `EricksonLopez.Outbox.Storage.MongoDb` | `src/EricksonLopez.Outbox.Storage.MongoDb/` | `net8.0`, `net9.0`, `net10.0` | Transactional `IOutboxRepository` for MongoDB (MongoDB.Driver) |
| `EricksonLopez.Outbox.Brokers.RabbitMQ` | `src/EricksonLopez.Outbox.Brokers.RabbitMQ/` | `net8.0`, `net9.0`, `net10.0` | `IBrokerPublisher` for RabbitMQ |
| `EricksonLopez.Outbox.Brokers.Kafka` | `src/EricksonLopez.Outbox.Brokers.Kafka/` | `net8.0`, `net9.0`, `net10.0` | `IBrokerPublisher` for Apache Kafka |
| `EricksonLopez.Outbox.Brokers.AzureServiceBus` | `src/EricksonLopez.Outbox.Brokers.AzureServiceBus/` | `net8.0`, `net9.0`, `net10.0` | `IBrokerPublisher` for Azure Service Bus |
| `EricksonLopez.Outbox.Brokers.AzureEventHubs` | `src/EricksonLopez.Outbox.Brokers.AzureEventHubs/` | `net8.0`, `net9.0`, `net10.0` | `IBrokerPublisher` for Azure Event Hubs |
| `EricksonLopez.Outbox.Brokers.AwsSqs` | `src/EricksonLopez.Outbox.Brokers.AwsSqs/` | `net8.0`, `net9.0`, `net10.0` | `IBrokerPublisher` for AWS SQS |
| `EricksonLopez.Outbox.Brokers.GooglePubSub` | `src/EricksonLopez.Outbox.Brokers.GooglePubSub/` | `net8.0`, `net9.0`, `net10.0` | `IBrokerPublisher` for Google Cloud Pub/Sub |
| `EricksonLopez.Outbox.Brokers.Nats` | `src/EricksonLopez.Outbox.Brokers.Nats/` | `net8.0`, `net9.0`, `net10.0` | `IBrokerPublisher` for NATS |
| `EricksonLopez.Outbox.Brokers.RedisStreams` | `src/EricksonLopez.Outbox.Brokers.RedisStreams/` | `net8.0`, `net9.0`, `net10.0` | `IBrokerPublisher` for Redis Streams |
| `EricksonLopez.Outbox.Aspire` | `src/EricksonLopez.Outbox.Aspire/` | `net8.0`, `net9.0`, `net10.0` | .NET Aspire host component integration for telemetry and health checks |
| `EricksonLopez.Outbox.Analyzers` | `src/EricksonLopez.Outbox.Analyzers/` | `netstandard2.0` | Roslyn analyzers (OUTBOX001-OUTBOX013) — dev-time only |
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
| ORM | `Microsoft.EntityFrameworkCore` | `9.0.18` |
| ORM | `Microsoft.EntityFrameworkCore.Relational` | `9.0.18` |
| Database | `Npgsql` | `10.0.3` |
| Database | `Microsoft.Data.SqlClient` | `7.0.2` |
| Database | `Microsoft.Data.Sqlite` | `10.0.10` |
| Database | `MySqlConnector` | `2.6.1` |
| Database | `Oracle.ManagedDataAccess.Core` | `23.4.0` |
| Database | `MongoDB.Driver` | `3.2.1` |
| Broker | `RabbitMQ.Client` | `7.1.1` |
| Broker | `Confluent.Kafka` | `2.15.0` |
| Broker | `Azure.Messaging.ServiceBus` | `7.20.2` |
| Broker | `Azure.Messaging.EventHubs` | `5.11.5` |
| Broker | `AWSSDK.SQS` | `4.0.100.7` |
| Broker | `Google.Cloud.PubSub.V1` | `3.36.0` |
| Broker | `NATS.Client.Core` | `3.1.0` |
| Broker | `StackExchange.Redis` | `2.8.0` |
| Broker | `MassTransit` | `9.2.0` |
| Mediator | `EricksonLopez.Mediator` | `1.0.0` |
| Events | `EricksonLopez.Events.Contracts` | `1.0.0` |
| Serializer | `protobuf-net` | `3.2.45` |
| Serializer | `MessagePack` | `3.1.3` |
| Roslyn | `Microsoft.CodeAnalysis.CSharp` | `5.6.0` |
| Build | `Microsoft.SourceLink.GitHub` | `10.0.301` |
| Testing | `AwesomeAssertions` | `9.5.0` |
| Testing | `xunit` | `2.9.3` |
| Testing | `Testcontainers` | `4.13.0` |
| Benchmarks | `BenchmarkDotNet` | `0.15.8` |

---

## 3. Semantic Versioning

The project adheres to **Semantic Versioning 2.0** (SemVer).

- The base version is managed in `Directory.Build.props` via `<VersionPrefix>` (current: `2.0.0`).
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
