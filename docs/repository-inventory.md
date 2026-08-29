<!-- Copyright © Erickson Lopez. MIT License. -->

# Repository Inventory

This document provides a comprehensive inventory of all projects, target frameworks, package classifications, and dependencies in the `dotnet-outbox` repository.

---

## 1. Projects and Target Frameworks

### 1.1. Core Library Projects (6 projects)

| Project | Target Framework(s) | Description | Classification |
| :--- | :--- | :--- | :--- |
| `EricksonLopez.Outbox` | `net8.0;net9.0;net10.0` | Core transactional outbox engine, background dispatcher, poller, pipeline, retry, diagnostics | **Core Library** |
| `EricksonLopez.Outbox.Abstractions` | `net8.0;net9.0;net10.0` | Foundational contracts (`IOutbox`, `IOutboxTransactionContext`, `IOutboxSerializer`, attributes) | **Core Library** |
| `EricksonLopez.Inbox` | `net8.0;net9.0;net10.0` | Standalone consumer deduplication and inbox engine (`IInboxStore`, `IdempotencyChecker`) | **Core Library** |
| `EricksonLopez.Inbox.Abstractions` | `net8.0;net9.0;net10.0` | Foundational contracts for consumer idempotency (`IdempotencyKey`, `IInboxEntry`) | **Core Library** |
| `EricksonLopez.Outbox.Events` | `net8.0;net9.0;net10.0` | Domain & integration events integration with `EricksonLopez.Events` (`OutboxEventPublisher`) | **Core Library** |
| `EricksonLopez.Outbox.Inbox.Events` | `net8.0;net9.0;net10.0` | Idempotent event handling pipeline (`IdempotentEventHandler<TEvent>`) | **Core Library** |

---

### 1.2. Infrastructure: Storage Providers (8 projects)

| Project | Target Framework(s) | Underlying Technology | Classification |
| :--- | :--- | :--- | :--- |
| `EricksonLopez.Outbox.Storage.PostgreSql` | `net8.0;net9.0;net10.0` | Npgsql / PostgreSQL raw ADO.NET with `SKIP LOCKED` | **Infrastructure** |
| `EricksonLopez.Outbox.Storage.SqlServer` | `net8.0;net9.0;net10.0` | Microsoft.Data.SqlClient / SQL Server with `ROWLOCK, READPAST` | **Infrastructure** |
| `EricksonLopez.Outbox.Storage.Sqlite` | `net8.0;net9.0;net10.0` | Microsoft.Data.Sqlite / SQLite for embedded / edge | **Infrastructure** |
| `EricksonLopez.Outbox.Storage.MySql` | `net8.0;net9.0;net10.0` | MySqlConnector / MySQL with `FOR UPDATE SKIP LOCKED` | **Infrastructure** |
| `EricksonLopez.Outbox.Storage.MariaDb` | `net8.0;net9.0;net10.0` | MySqlConnector / MariaDB | **Infrastructure** |
| `EricksonLopez.Outbox.Storage.Oracle` | `net8.0;net9.0;net10.0` | Oracle.ManagedDataAccess.Core / Oracle Database | **Infrastructure** |
| `EricksonLopez.Outbox.Storage.MongoDb` | `net8.0;net9.0;net10.0` | MongoDB.Driver / Document Outbox | **Infrastructure** |
| `EricksonLopez.Outbox.EntityFrameworkCore` | `net8.0;net9.0;net10.0` | Microsoft.EntityFrameworkCore generic provider | **Infrastructure** |

---

### 1.3. Infrastructure: Broker Publishers (8 projects)

| Project | Target Framework(s) | Client Driver | Classification |
| :--- | :--- | :--- | :--- |
| `EricksonLopez.Outbox.Brokers.RabbitMQ` | `net8.0;net9.0;net10.0` | `RabbitMQ.Client` 7.x | **Infrastructure** |
| `EricksonLopez.Outbox.Brokers.Kafka` | `net8.0;net9.0;net10.0` | `Confluent.Kafka` 2.x | **Infrastructure** |
| `EricksonLopez.Outbox.Brokers.AzureServiceBus` | `net8.0;net9.0;net10.0` | `Azure.Messaging.ServiceBus` 7.x | **Infrastructure** |
| `EricksonLopez.Outbox.Brokers.AzureEventHubs` | `net8.0;net9.0;net10.0` | `Azure.Messaging.EventHubs` 5.x | **Infrastructure** |
| `EricksonLopez.Outbox.Brokers.AwsSqs` | `net8.0;net9.0;net10.0` | `AWSSDK.SQS` 3.x | **Infrastructure** |
| `EricksonLopez.Outbox.Brokers.GooglePubSub` | `net8.0;net9.0;net10.0` | `Google.Cloud.PubSub.V1` 3.x | **Infrastructure** |
| `EricksonLopez.Outbox.Brokers.Nats` | `net8.0;net9.0;net10.0` | `NATS.Client.Core` 2.x | **Infrastructure** |
| `EricksonLopez.Outbox.Brokers.RedisStreams` | `net8.0;net9.0;net10.0` | `StackExchange.Redis` 2.x | **Infrastructure** |

---

### 1.4. Infrastructure: Ecosystem Integrations & Serialization (12 projects)

| Project | Target Framework(s) | Integration Target | Classification |
| :--- | :--- | :--- | :--- |
| `EricksonLopez.Outbox.MassTransit` | `net8.0;net9.0;net10.0` | MassTransit 8.x (`IPublishEndpoint`, `InboxIdempotencyFilter`) | **Infrastructure** |
| `EricksonLopez.Outbox.Mediator` | `net8.0;net9.0;net10.0` | `EricksonLopez.Mediator` zero-allocation mediator | **Infrastructure** |
| `EricksonLopez.Outbox.MediatR` | `net8.0;net9.0;net10.0` | MediatR 12.x/14.x notification publisher | **Infrastructure** |
| `EricksonLopez.Outbox.NServiceBus` | `net8.0;net9.0;net10.0` | NServiceBus 8.x/10.x pipeline behavior | **Infrastructure** |
| `EricksonLopez.Outbox.Rebus` | `net8.0;net9.0;net10.0` | Rebus 8.x outgoing pipeline step | **Infrastructure** |
| `EricksonLopez.Outbox.Brighter` | `net8.0;net9.0;net10.0` | Paramore.Brighter command processor | **Infrastructure** |
| `EricksonLopez.Outbox.Dapr` | `net8.0;net9.0;net10.0` | Dapr Pub/Sub component integration | **Infrastructure** |
| `EricksonLopez.Outbox.Aspire` | `net8.0;net9.0;net10.0` | .NET Aspire hosting and service defaults | **Infrastructure** |
| `EricksonLopez.Outbox.Inbox` | `net8.0;net9.0;net10.0` | Outbox-to-Inbox bridge deduplication filter | **Infrastructure** |
| `EricksonLopez.Outbox.Inbox.AspNetCore` | `net8.0;net9.0;net10.0` | ASP.NET Core `Idempotency-Key` HTTP endpoint filter | **Infrastructure** |
| `EricksonLopez.Outbox.Serialization.MessagePack` | `net8.0;net9.0;net10.0` | MessagePack binary serializer | **Infrastructure** |
| `EricksonLopez.Outbox.Serialization.Protobuf` | `net8.0;net9.0;net10.0` | Protocol Buffers (protobuf-net) binary serializer | **Infrastructure** |

---

### 1.5. Internal & Compiler Tooling (2 projects)

| Project | Target Framework | Roslyn / Engine | Classification |
| :--- | :--- | :--- | :--- |
| `EricksonLopez.Outbox.Analyzers` | `netstandard2.0` | Roslyn Diagnostics (`OUTBOX001` to `OUTBOX013`) | **Internal / Tooling** |
| `EricksonLopez.Outbox.SourceGenerators` | `netstandard2.0` | Roslyn IIncrementalGenerator for `[OutboxMessage]` | **Internal / Tooling** |

---

### 1.6. Samples / Showcase (1 project)

| Project | Target Framework | Description | Classification |
| :--- | :--- | :--- | :--- |
| `Sample.OrderService` | `net10.0` (Native AOT) | Official executable reference implementation and learning guide | **Samples (Showcase)** |

---

### 1.7. Benchmarks (1 project)

| Project | Target Framework | Benchmark Harness | Classification |
| :--- | :--- | :--- | :--- |
| `EricksonLopez.Outbox.Benchmarks` | `net10.0` | BenchmarkDotNet micro & macro benchmarks | **Benchmarks** |

---

### 1.8. Test Projects (36 projects)

| Test Project | SUT / Target Area | Classification |
| :--- | :--- | :--- |
| `EricksonLopez.Outbox.Tests` | Core engine, poller, builder, channel, health checks, testing APIs | **Tests** |
| `EricksonLopez.Outbox.AotSmokeTest` | Native AOT compilation & smoke execution | **Tests** |
| `EricksonLopez.Outbox.IntegrationTests` | End-to-end database + broker integration (Testcontainers) | **Tests** |
| `EricksonLopez.Inbox.Tests` | Standalone inbox deduplication tests | **Tests** |
| `EricksonLopez.Outbox.Analyzers.Tests` | Roslyn analyzer diagnostic and codefix tests | **Tests** |
| `EricksonLopez.Outbox.SourceGenerators.Tests` | Roslyn incremental source generator tests | **Tests** |
| `EricksonLopez.Outbox.EntityFrameworkCore.Tests` | EF Core outbox repository & model builder tests | **Tests** |
| `EricksonLopez.Outbox.Storage.PostgreSql.Tests` | PostgreSQL raw ADO.NET repository tests | **Tests** |
| `EricksonLopez.Outbox.Storage.SqlServer.Tests` | SQL Server raw ADO.NET repository tests | **Tests** |
| `EricksonLopez.Outbox.Storage.Sqlite.Tests` | SQLite raw ADO.NET repository tests | **Tests** |
| `EricksonLopez.Outbox.Storage.MySql.Tests` | MySQL raw ADO.NET repository tests | **Tests** |
| `EricksonLopez.Outbox.Storage.MariaDb.Tests` | MariaDB raw ADO.NET repository tests | **Tests** |
| `EricksonLopez.Outbox.Storage.Oracle.Tests` | Oracle Database raw ADO.NET repository tests | **Tests** |
| `EricksonLopez.Outbox.Storage.MongoDb.Tests` | MongoDB document outbox repository tests | **Tests** |
| `EricksonLopez.Outbox.Brokers.RabbitMQ.Tests` | RabbitMQ broker publisher tests | **Tests** |
| `EricksonLopez.Outbox.Brokers.Kafka.Tests` | Apache Kafka broker publisher tests | **Tests** |
| `EricksonLopez.Outbox.Brokers.AzureServiceBus.Tests` | Azure Service Bus publisher tests | **Tests** |
| `EricksonLopez.Outbox.Brokers.AzureEventHubs.Tests` | Azure Event Hubs publisher tests | **Tests** |
| `EricksonLopez.Outbox.Brokers.AwsSqs.Tests` | AWS SQS broker publisher tests | **Tests** |
| `EricksonLopez.Outbox.Brokers.GooglePubSub.Tests` | Google Cloud Pub/Sub publisher tests | **Tests** |
| `EricksonLopez.Outbox.Brokers.Nats.Tests` | NATS Core broker publisher tests | **Tests** |
| `EricksonLopez.Outbox.Brokers.RedisStreams.Tests` | Redis Streams broker publisher tests | **Tests** |
| `EricksonLopez.Outbox.MassTransit.Tests` | MassTransit filter & publisher tests | **Tests** |
| `EricksonLopez.Outbox.Mediator.Tests` | EricksonLopez.Mediator notification handler tests | **Tests** |
| `EricksonLopez.Outbox.MediatR.Tests` | MediatR notification publisher tests | **Tests** |
| `EricksonLopez.Outbox.NServiceBus.Tests` | NServiceBus pipeline behavior tests | **Tests** |
| `EricksonLopez.Outbox.Rebus.Tests` | Rebus outgoing step tests | **Tests** |
| `EricksonLopez.Outbox.Brighter.Tests` | Paramore.Brighter processor tests | **Tests** |
| `EricksonLopez.Outbox.Dapr.Tests` | Dapr pubsub publisher tests | **Tests** |
| `EricksonLopez.Outbox.Aspire.Tests` | .NET Aspire configuration tests | **Tests** |
| `EricksonLopez.Outbox.Inbox.Tests` | Outbox inbox deduplication tests | **Tests** |
| `EricksonLopez.Outbox.Inbox.AspNetCore.Tests` | ASP.NET Core HTTP filter tests | **Tests** |
| `EricksonLopez.Outbox.Inbox.Events.Tests` | Idempotent event handler tests | **Tests** |
| `EricksonLopez.Outbox.Events.Tests` | Event publisher transactional outbox tests | **Tests** |
| `EricksonLopez.Outbox.Serialization.MessagePack.Tests` | MessagePack serialization tests | **Tests** |
| `EricksonLopez.Outbox.Serialization.Protobuf.Tests` | Protobuf serialization tests | **Tests** |

---

## 2. Quality & Security Gates

| Tool | Configuration | Enforcement |
| :--- | :--- | :--- |
| **Code Coverage** | `coverlet.runsettings` | Line Coverage ≥ 90%, Branch Coverage ≥ 80% |
| **Mutation Testing** | `mutation-testing.yml` + 34 Stryker configs | Break Gate < 95%, Low Threshold ≥ 98%, High Target = 100% |
| **Release Mutation Gate** | `scripts/verify-mutation-gate.js` | Enforces 95% threshold before packaging & release |
| **Roslyn Analyzers** | `EricksonLopez.Outbox.Analyzers` | Build-time errors (`OUTBOX001`–`OUTBOX013`) |
| **Code Style** | `EnforceCodeStyleInBuild=true` in `Directory.Build.props` | All IDE style rules strictly enforced |
| **Native AOT Validation** | `IsAotCompatible=true`, `PublishAot=true` | Zero trim warnings, dedicated CI smoke test |
| **Strong Naming** | `EricksonLopez.snk` | All shipped assemblies signed |
| **Provenance & Attestation**| Sigstore (`actions/attest-build-provenance@v2`) | Cryptographic build provenance for NuGet packages |
| **Trusted Publishing** | `NuGet/login@v1` | Keyless OIDC authentication to NuGet.org |

