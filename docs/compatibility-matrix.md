# Compatibility Matrix

This document outlines the compatibility between the `EricksonLopez.Outbox` core library, various message brokers, database providers, and .NET Target Framework Monikers (TFMs).

## Framework Support Policy

> [!IMPORTANT]
> **This library supports only .NET frameworks with active official Microsoft support.**
>
> A framework version is added to `TargetFrameworks` when it enters active support, and is removed when it reaches its official **end-of-life (EOL) date as published by Microsoft** at [dotnet.microsoft.com/platform/support/policy/dotnet-core](https://dotnet.microsoft.com/platform/support/policy/dotnet-core).
>
> Framework versions are removed on or after their EOL date — not before. STS (Standard-Term Support) frameworks receive the same treatment as LTS (Long-Term Support) frameworks: they remain in `TargetFrameworks` for their full official support window.

| Framework | Type | Microsoft Support End Date | Current Status |
|---|---|---|---|
| .NET 8 | LTS | November 10, 2026 | ✅ **Active** |
| .NET 9 | STS | **November 10, 2026** | ✅ **Active** |
| .NET 10 | LTS | November 2028 | ✅ **Active** |

> **Note on .NET 9 STS**: .NET 9 is a Standard-Term Support release with a **24-month** support window (Microsoft policy update effective with .NET 9). Its end-of-support date is **November 10, 2026**, coinciding with .NET 8 LTS. This library will continue targeting `net9.0` for its full official support window. Users are encouraged to plan migration to .NET 10 (LTS, supported through November 2028) before that date.


## Package Target Frameworks

| Package | .NET 8.0 | .NET 9.0 | .NET 10.0 | NativeAOT Ready |
|---|:---:|:---:|:---:|:---:|
| `EricksonLopez.Outbox` (Core) | ✅ | ✅ | ✅ | ✅ |
| `EricksonLopez.Outbox.EntityFrameworkCore` | ✅ | ✅ | ✅ | ⚠️ (EF Core limitation) |
| `EricksonLopez.Outbox.Storage.*` (all 5) | ✅ | ✅ | ✅ | ✅ |
| `EricksonLopez.Outbox.Brokers.*` (all 7) | ✅ | ✅ | ✅ | ⚠️ (varies by broker SDK) |
| `EricksonLopez.Outbox.MassTransit` | ✅ | ✅ | ✅ | ⚠️ (MassTransit limitation) |
| `EricksonLopez.Outbox.SourceGenerators` | `netstandard2.0` | `netstandard2.0` | `netstandard2.0` | N/A (compile tool) |
| `EricksonLopez.Outbox.Analyzers` | `netstandard2.0` | `netstandard2.0` | `netstandard2.0` | N/A (dev tool) |

> [!NOTE]
> **NativeAOT**: The core library and all Storage packages are 100% NativeAOT
> compliant. `EricksonLopez.Outbox.EntityFrameworkCore` and `EricksonLopez.Outbox.MassTransit`
> depend on frameworks that use Reflection internally, limiting NativeAOT compatibility.
> For pure NativeAOT microservices, use a Storage package (e.g., `Storage.PostgreSql`) directly
> with a NativeAOT-compatible broker (e.g., `Brokers.RabbitMQ` or `Brokers.Kafka`).

## Database Providers Compatibility

The library supports multiple database engines via raw ADO.NET storage providers.
The performance guarantees and feature sets vary slightly depending on the engine's
locking capabilities.

| Database Engine | Storage Package | ADO.NET Driver | Concurrency Strategy | Recommended for Prod |
|---|---|---|---|:---:|
| **PostgreSQL** | `Storage.PostgreSql` | `Npgsql` | `FOR UPDATE SKIP LOCKED` | ⭐ Highly Recommended |
| **SQL Server** | `Storage.SqlServer` | `Microsoft.Data.SqlClient` | `WITH (UPDLOCK, READPAST)` | ✅ Recommended |
| **MySQL** | `Storage.MySql` | `MySqlConnector` | `FOR UPDATE SKIP LOCKED` (MySQL 8.0+) | ✅ Recommended |
| **Oracle** | `Storage.Oracle` | `Oracle.ManagedDataAccess.Core` | `FOR UPDATE SKIP LOCKED` (12c+) | ✅ Recommended |
| **SQLite** | `Storage.Sqlite` | `Microsoft.Data.Sqlite` | Table-level locking (WAL mode) | ⚠️ Dev/Testing Only |

## Message Brokers Compatibility

Each broker has its own dedicated package under `EricksonLopez.Outbox.Brokers.*`.

| Broker | Package | Underlying SDK | Delivery Semantics |
|---|---|---|---|
| **RabbitMQ** | `Brokers.RabbitMQ` | `RabbitMQ.Client` `7.1.1` | At-Least-Once |
| **Apache Kafka** | `Brokers.Kafka` | `Confluent.Kafka` `2.3.0` | At-Least-Once |
| **Azure Service Bus** | `Brokers.AzureServiceBus` | `Azure.Messaging.ServiceBus` `7.17.4` | At-Least-Once |
| **AWS SQS** | `Brokers.AwsSqs` | `AWSSDK.SQS` `3.7.300.73` | At-Least-Once |
| **Google Pub/Sub** | `Brokers.GooglePubSub` | `Google.Cloud.PubSub.V1` `3.23.0` | At-Least-Once |
| **NATS** | `Brokers.Nats` | `NATS.Client.Core` `2.5.5` | At-Least-Once |
| **Redis Streams** | `Brokers.RedisStreams` | `StackExchange.Redis` `2.8.0` | At-Least-Once |

## ORM and Data Access Compatibility

| ORM / Library | Integration Package | Performance Profile |
|---|---|---|
| **Entity Framework Core** | `EricksonLopez.Outbox.EntityFrameworkCore` | Excellent developer experience, standard DbContext integration, slightly higher memory overhead. |
| **Raw ADO.NET** | `EricksonLopez.Outbox.Storage.*` | Maximum throughput, zero-allocation hot paths, pure ADO.NET with no ORM overhead. |

## Framework Integrations

| Framework | Integration Package | Description |
|---|---|---|
| **MassTransit** | `EricksonLopez.Outbox.MassTransit` | `MassTransitBrokerPublisher` adapter and `InboxIdempotencyFilter` for MassTransit consumers. |
