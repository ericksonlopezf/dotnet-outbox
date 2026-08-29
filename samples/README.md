<!-- Copyright © Erickson Lopez. MIT License. -->

# Sample Applications — EricksonLopez.Outbox

This directory contains executable reference applications and showcase implementations demonstrating real-world usage of `EricksonLopez.Outbox` and `EricksonLopez.Inbox`.

---

## `Sample.OrderService`

`Sample.OrderService` is a modern, high-performance ASP.NET Core Minimal API service with NativeAOT support that demonstrates how to implement the Transactional Outbox and Idempotent Inbox patterns with PostgreSQL, RabbitMQ, Seq (OpenTelemetry), and health checks.

### Technology Stack

- **Runtime**: .NET 10.0 (Native AOT enabled)
- **Database**: PostgreSQL 15+ (using raw ADO.NET and EF Core providers)
- **Message Broker**: RabbitMQ 3.x
- **Observability**: OpenTelemetry metrics, tracing, and structured logging exported to Seq via OTLP gRPC (`http://seq:4317`)
- **Health Checks**: ASP.NET Core Health Checks for PostgreSQL and RabbitMQ

### Running the Sample Locally

#### 1. Start Infrastructure via Docker Compose

```bash
docker-compose -f samples/Sample.OrderService/docker-compose.yml up -d
```

This starts:
- **PostgreSQL**: `localhost:5432` (`user=postgres`, `password=postgres`, `db=outbox_showcase`)
- **RabbitMQ Management**: `http://localhost:15672` (`guest:guest`)
- **Seq Telemetry UI**: `http://localhost:5341`

#### 2. Run the Service

```bash
cd samples/Sample.OrderService
dotnet run -c Release
```

The service will be available at `http://localhost:5000` (or `https://localhost:5001`).

---

## Showcase Level Mapping

`Sample.OrderService` serves as the executable backing implementation for the 14-level progressive tutorial:

| Level | Topic | Documentation Link |
|---|---|---|
| Level 00 | Introduction to the Outbox Pattern | [docs/showcase/level-00-introduction.md](../docs/showcase/level-00-introduction.md) |
| Level 01 | Getting Started & Basic Outbox | [docs/showcase/level-01-getting-started.md](../docs/showcase/level-01-getting-started.md) |
| Level 02 | Advanced Configuration & Options | [docs/showcase/level-02-configuration.md](../docs/showcase/level-02-configuration.md) |
| Level 03 | Real-World Use Cases (Orders, Payments) | [docs/showcase/level-03-real-use-cases.md](../docs/showcase/level-03-real-use-cases.md) |
| Level 04 | Domain Events & Integration Events | [docs/showcase/level-04-domain-events.md](../docs/showcase/level-04-domain-events.md) |
| Level 05 | High-Throughput Processing & Dispatch | [docs/showcase/level-05-processing.md](../docs/showcase/level-05-processing.md) |
| Level 06 | Error Handling, Retries & Dead Letter Queue | [docs/showcase/level-06-error-handling.md](../docs/showcase/level-06-error-handling.md) |
| Level 07 | Horizontal Scalability & SKIP LOCKED | [docs/showcase/level-07-scalability.md](../docs/showcase/level-07-scalability.md) |
| Level 08 | Custom Middleware & Serialization | [docs/showcase/level-08-customization.md](../docs/showcase/level-08-customization.md) |
| Level 09 | Framework Extensions (MassTransit, Mediator) | [docs/showcase/level-09-extensions.md](../docs/showcase/level-09-extensions.md) |
| Level 10 | Enterprise Architecture & Multi-Tenancy | [docs/showcase/level-10-enterprise-architecture.md](../docs/showcase/level-10-enterprise-architecture.md) |
| Level 11 | Administration & Periodic Purging | [docs/showcase/level-11-administration.md](../docs/showcase/level-11-administration.md) |
| Level 12 | Unit, Integration & Chaos Testing | [docs/showcase/level-12-testing.md](../docs/showcase/level-12-testing.md) |
| Level 13 | OpenTelemetry Diagnostics & Metrics | [docs/showcase/level-13-diagnostics.md](../docs/showcase/level-13-diagnostics.md) |
