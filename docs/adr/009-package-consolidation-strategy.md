<!-- Copyright © Erickson Lopez. MIT License. -->

# ADR-009: Package Consolidation Strategy

## 1. Title and Status

**Publish Per-Provider Packages, Not Consolidated Packages**
*Status:* Accepted and Implemented

---

## 2. Context and Motivation

The `EricksonLopez.Outbox` ecosystem supports 5 database engines (PostgreSQL,
SQL Server, MySQL, Oracle, SQLite) and 7 message brokers (RabbitMQ, Kafka,
Azure Service Bus, AWS SQS, Google Pub/Sub, NATS, Redis Streams). A naive
packaging approach would produce one NuGet package per provider:

- `EricksonLopez.Outbox.Storage.PostgreSql`
- `EricksonLopez.Outbox.Storage.SqlServer`
- `EricksonLopez.Outbox.Brokers.RabbitMQ`
- `EricksonLopez.Outbox.Brokers.Kafka`
- … (12+ packages total)

This was originally rejected in favor of consolidated packages, but after further review of the actual codebase, we have implemented the per-provider packages:

- `EricksonLopez.Outbox.Storage.PostgreSql`, etc.
- `EricksonLopez.Outbox.Brokers.RabbitMQ`, etc.

---

## 3. Evaluated Alternatives

### Option A: Per-Provider Packages (Chosen and Implemented)
- `EricksonLopez.Outbox.Storage.PostgreSql`, `...SqlServer`, etc.
- `EricksonLopez.Outbox.Brokers.RabbitMQ`, `...Kafka`, etc.

**Advantages**: Minimal transitive dependency graph — a PostgreSQL-only user would
not download SQL Server drivers.

**Disadvantages**:
- 12+ NuGet package IDs to maintain, version, and publish.
- Complex CI/CD — each package needs independent versioning and compatibility validation.
- High developer friction — `dotnet add package EricksonLopez.Outbox.Storage.PostgreSql`
  requires prior knowledge of the package naming scheme.
- Breaking changes in one driver require a new release of only that package,
  complicating the compatibility matrix.

### Option B: Monolith (Rejected)
Everything (Core + Storage + Brokers) in a single assembly.

**Disadvantages**: Forces all consumers to take all dependencies regardless of usage.
No separation of concerns. Impossible to use Core-only without Storage or Broker deps.

### Option C: Consolidated Packages (Initially Chosen, then Rejected)
- `EricksonLopez.Outbox` — Core + Dispatcher only.
- `EricksonLopez.Outbox.Storage` — all DB implementations.
- `EricksonLopez.Outbox.Brokers` — all broker implementations.
- Separate packages only where the dependency graph is fundamentally different
  (EF Core, MassTransit, Dapper).

---

## 4. Rationale

1. **Clean Dependency Graph:** By isolating each database and broker into its own project, consumers only download the transitive dependencies they actually need (e.g., `Npgsql` for PostgreSQL, `Confluent.Kafka` for Kafka). They are not forced to download a massive consolidated package.
2. **Security & Vulnerability Isolation:** If a specific broker SDK (e.g., RabbitMQ.Client) reports a CVE, only the consumers of `EricksonLopez.Outbox.Brokers.RabbitMQ` are affected. A consolidated package would force all users to urgently update, regardless of whether they use RabbitMQ or not.
3. **Semantic Versioning Granularity:** If a breaking change occurs in the AWS SQS SDK, we can bump the major version of the `EricksonLopez.Outbox.Brokers.AwsSqs` package without breaking the compatibility matrix for the rest of the ecosystem.
4. **Developer Trust:** Developers are accustomed to installing specific provider packages (e.g., `Microsoft.EntityFrameworkCore.SqlServer`). Adopting this pattern reduces cognitive load and aligns with the .NET ecosystem conventions.

---

## 5. Trade-offs and Accepted Costs

| Cost | Mitigation |
|---|---|
| Maintaining 12+ NuGet packages and `.csproj` files | Using Central Package Management (`Directory.Packages.props`) ensures dependency versions are synced globally. |
| Complex CI/CD Publish Pipeline | The `publish.yml` workflow can utilize `dotnet pack EricksonLopez.Outbox.slnx` or wildcard paths to build all projects simultaneously, avoiding verbose scripts. |
| Potential for mismatched versions if users update packages independently | The ecosystem uses a unified `VersionPrefix` defined in `Directory.Build.props`. All packages are released simultaneously with the same version number, guaranteeing mutual compatibility. |

---

## 6. Impact on the Publish Workflow

The `publish.yml` workflow must correctly pack all the individual per-provider projects in the `src/` directory. Explicitly hardcoding paths in the `dotnet pack` step for 15+ projects is error-prone. The CI/CD pipeline should be updated to pack the entire solution (`.slnx`) or use wildcard paths to ensure no provider package is left behind during a release.

---

## 7. Consequences

- All future storage implementations must be added as a distinct project in `src/` (e.g., `src/EricksonLopez.Outbox.Storage.CockroachDb/`).
- All future broker implementations must be added as a distinct project in `src/` (e.g., `src/EricksonLopez.Outbox.Brokers.ActiveMQ/`).
- The README, docs, and quickstart guides must accurately list the available per-provider packages and their NuGet links.
- The `publish.yml` CI/CD workflow must be configured to pack and publish all these projects to NuGet.org upon a new release.
