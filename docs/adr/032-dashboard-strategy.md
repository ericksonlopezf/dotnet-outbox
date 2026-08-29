<!-- Copyright © Erickson Lopez. MIT License. -->

# ADR-032 — Dashboard & Operations UI Strategy

## Status

Accepted

## Context

Operational visibility is crucial for enterprise messaging systems. Competitors like DotNetCore.CAP bundle a web UI dashboard into the middleware pipeline. However, embedding HTML/JS/CSS assets, controllers, and static file middleware into a high-performance messaging core introduces significant bloat, framework coupling (ASP.NET Core web stack), security attack surface, and maintenance overhead.

## Decision

1. **No Web UI in Core**: The core `EricksonLopez.Outbox` and storage packages will remain strictly focused on the outbox execution engine and will NOT embed UI assets or web servers.
2. **OpenTelemetry & Standard Observability First**: `EricksonLopez.Outbox` emits comprehensive metrics (`System.Diagnostics.Metrics`), distributed traces (`ActivitySource`), and structured logs. Operations teams can visualize real-time queues, dispatch rates, lag, and DLQ counts directly in standard dashboards (Grafana, Prometheus, Datadog, Azure Application Insights).
3. **Dedicated Extension Package**: If an operational UI or administrative REST API is demanded, it will be designed as a distinct optional package (`EricksonLopez.Outbox.Dashboard`) completely decoupled from the runtime execution engine.

## Rationale

1. Adheres to the Single Responsibility Principle and maintains minimal runtime footprint.
2. Prevents forcing web server dependencies onto console applications, background workers, or AWS Lambda / Azure Functions consumers.
3. Aligns with modern cloud-native observability standards (OpenTelemetry/Grafana) rather than bespoke single-purpose web pages.

## Consequences

### Positive
- Zero UI bloat or security vulnerabilities in production core packages.
- Seamless integration with enterprise monitoring stacks (Grafana/Datadog).

### Negative
- Developers wanting an out-of-the-box local web page for dev environments utilize OTel dashboards or community dashboard templates.

## Related ADRs

- ADR-001: Monorepo Modular Structure
- ADR-020: No Broker Dependency in Core
