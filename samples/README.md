<!-- Copyright © Erickson Lopez. MIT License. -->

# Sample Applications & Reference Implementations

This directory contains executable reference applications and showcase implementations demonstrating real-world integration of `EricksonLopez.Outbox` and `EricksonLopez.Inbox` across full microservice architectures.

---

## 1. `Sample.OrderService` Overview

`Sample.OrderService` is an enterprise-grade ASP.NET Core Minimal API service demonstrating NativeAOT compatibility, the Transactional Outbox pattern, Idempotent Consumer (Inbox) patterns, OpenTelemetry diagnostics, and health monitoring.

### Key Capabilities Demonstrated
- **Transactional Consistency**: Atomically persists business state and outbox messages using raw ADO.NET (`PostgreSqlOutboxRepository`) and EF Core interceptors (`PublishDomainEventsInterceptor`).
- **Idempotent Ingestion**: Enforces deduplication using `EricksonLopez.Outbox.Inbox` and HTTP `Idempotency-Key` headers (`EricksonLopez.Outbox.Inbox.AspNetCore`).
- **Asynchronous Polling & Dispatch**: Background worker draining bounded channels with adaptive polling and concurrency controls.
- **Dead-Letter Resiliency**: Captures exhausted failures with compile-time regex credential sanitization (`DefaultErrorSanitizer` / `CustomErrorSanitizer`).
- **Telemetry & Tracing**: Native OpenTelemetry instrumentation exporting traces and metrics via OTLP to Seq.

---

## 2. Infrastructure & Local Deployment

A complete local development environment is provided via Docker Compose (`samples/Sample.OrderService/docker-compose.yml`).

### External Services Architecture

| Service | Image | Internal Port | Host Port | Purpose |
|---|---|---|---|---|
| **PostgreSQL** | `postgres:15-alpine` | `5432` | `5432` | Relational database holding outbox and dead-letter tables |
| **RabbitMQ** | `rabbitmq:3-management-alpine` | `5672`, `15672` | `5672`, `15672` | Message broker with web management dashboard |
| **Seq** | `datalust/seq:latest` | `80`, `4317` | `5341`, `4317` | OpenTelemetry (OTLP) log, trace, and metric ingestion |
| **OrderService** | Custom (multi-stage) | `8080` | `5000` | Sample API running in container |

### Quick Start with Docker Compose

```bash
# Start backing infrastructure and the service
docker-compose -f samples/Sample.OrderService/docker-compose.yml up -d
```

Access services locally:
- **Order Service API / Swagger UI**: `http://localhost:5000/swagger`
- **Health Checks Endpoint**: `http://localhost:5000/health`
- **RabbitMQ Management**: `http://localhost:15672` (Username: `guest`, Password: `guest`)
- **Seq Telemetry UI**: `http://localhost:5341`

---

## 3. Container Image (Dockerfile)

`Sample.OrderService/Dockerfile` uses a multi-stage build optimized for minimal container footprint and security:

1. **Build Stage (`mcr.microsoft.com/dotnet/sdk:10.0`)**: Restores centralized package dependencies via CPM (`Directory.Packages.props`), compiles with `Release` optimizations, and produces publish output.
2. **Runtime Stage (`mcr.microsoft.com/dotnet/aspnet:10.0`)**: Lightweight ASP.NET runtime image exposing internal port `8080`.
3. **Non-Root Execution**: Runs under the secure, unprivileged system user `USER $APP_UID`.

```bash
# Build the container image manually
docker build -t sample-orderservice:latest -f samples/Sample.OrderService/Dockerfile .

# Run the container image
docker run -d -p 5000:8080 \
  -e ConnectionStrings__Postgres="Host=host.docker.internal;Database=outbox_showcase;Username=postgres;Password=postgres" \
  -e OTEL_EXPORTER_OTLP_ENDPOINT="http://host.docker.internal:4317" \
  sample-orderservice:latest
```

---

## 4. Configuration & Environment Variables

The application resolves configuration dynamically from command-line arguments, JSON settings, and environment variables:

| Environment Variable | Default Value | Description |
|---|---|---|
| `ConnectionStrings__Postgres` | `Host=localhost;Database=outbox_showcase;Username=postgres;Password=postgres` | PostgreSQL connection string for ADO.NET and EF Core |
| `OTEL_EXPORTER_OTLP_ENDPOINT` | `http://localhost:4317` | Target OTLP gRPC endpoint for OpenTelemetry traces and metrics |
| `ASPNETCORE_ENVIRONMENT` | `Production` | Hosting environment (`Development`, `Staging`, `Production`) |
| `ASPNETCORE_URLS` | `http://+:8080` | Listening endpoints inside container |

---

## 5. Observability & Health Monitoring

### Health Checks (`/health`)
The service exposes an aggregated health check endpoint configured via `AddHealthChecks()`:
- **PostgreSQL Check**: Verifies database connectivity using `AddNpgSql()`.
- **Outbox Poller Check**: Verifies dispatcher state (`AddOutbox()`). Reports `Degraded` if pending messages exceed 500, and `Unhealthy` if background dispatch workers terminate unexpectedly.

```json
{
  "status": "Healthy",
  "checks": [
    { "name": "npgsql", "status": "Healthy", "description": null },
    { "name": "outbox", "status": "Healthy", "description": null }
  ]
}
```

### OpenTelemetry Telemetry
Configured in `Program.cs` with zero-reflection instrumentation:
- **Tracing**: Instruments incoming ASP.NET Core requests (`AddAspNetCoreInstrumentation`), outgoing HTTP calls (`AddHttpClientInstrumentation`), and Npgsql database commands (`AddNpgsql`).
- **Metrics**: Captures request rates, outbox queue lag, channel capacity, and publish latencies.
- **OTLP Exporter**: Exports metrics and traces directly to Seq via gRPC (`AddOtlpExporter()`).

---

## 6. Showcase Progressive Learning Guide

`Sample.OrderService` implements the progressive 14-level architectural learning curriculum:

| Level | Topic | Documentation Reference |
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
