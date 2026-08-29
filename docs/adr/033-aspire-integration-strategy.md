<!-- Copyright © Erickson Lopez. MIT License. -->

# ADR-033 — .NET Aspire Integration Strategy

## Status

Accepted

## Context

.NET Aspire is Microsoft's opinionated, cloud-ready stack for building observable, production-ready distributed applications in .NET 8+. It provides standardized service defaults, OpenTelemetry telemetry wiring, and health check discovery.

As .NET Aspire adoption accelerates, developers require seamless integration to configure `EricksonLopez.Outbox` instances within Aspire Host and App projects without boilerplate.

## Decision

We introduce `EricksonLopez.Outbox.Aspire` as an official integration component for .NET Aspire with the following design:

1. **Host & Client Extensions**: Provide `builder.AddOutbox(...)` extension methods that hook into Aspire's `IHostApplicationBuilder`.
2. **Automatic Telemetry & Health Checks**: Automatically register `OutboxHealthCheck` with Aspire's health check pipeline and configure `OutboxMetrics` and `OutboxActivitySource` with Aspire's OpenTelemetry meters and tracers.
3. **100% NativeAOT Compatible**: Avoid reflection or dynamic assembly discovery, preserving complete trim and AOT readiness.

## Rationale

1. Delivers first-class developer experience for .NET Aspire users.
2. Eliminates manual wiring of health checks, meters, and trace sources.
3. Maintains clean separation of Aspire hosting concerns from the core outbox engine.

## Consequences

### Positive
- One-line setup in .NET Aspire applications.
- Automatic integration with Aspire dashboard telemetry.

### Negative
- Additional package to maintain alongside .NET Aspire SDK updates.

## Related ADRs

- ADR-001: Monorepo Modular Structure
- ADR-023: Serialization Pluggable AOT First
