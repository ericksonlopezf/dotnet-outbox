<!-- Copyright © Erickson Lopez. MIT License. -->

# ADR-036: Legacy MediatR Adapter AOT Non-Compatibility and Staged Deprecation Strategy

## Status
Accepted — August 2026

## Context
`EricksonLopez.Outbox.MediatR` provides an adapter enabling `MediatR` notification publishing to route events into the transactional Outbox table.

Because third-party `MediatR` relies on runtime reflection and dynamic type activation, this package cannot satisfy Native AOT and Trimming guarantees enforced across the `EricksonLopez.*` ecosystem.

## Decision

### 1. Explicit AOT/Trimming Disabling
- Enforce `<IsAotCompatible>false</IsAotCompatible>` and `<IsTrimmable>false</IsTrimmable>` in `EricksonLopez.Outbox.MediatR.csproj`.

### 2. Staged Deprecation Schedule

| Version | Status | Architectural Action |
|---|---|---|
| **v1.x (Current)** | Legacy Supported | Maintained for bugfixes; explicitly marked as non-AOT. Documentation recommends migration to `EricksonLopez.Outbox.Mediator` + `EricksonLopez.Mediator`. |
| **v2.0** | Final Migration Window | Enhanced migration guides and documentation warnings. No compile-time `[Obsolete]` to avoid noise in existing stable builds. |
| **v3.0.0** | Deprecated (`[Obsolete]`) | Public APIs decorated with `[Obsolete]` attribute (`IsError = false`, `DiagnosticId = "ELMED002"`). Non-breaking compiler warning. |
| **v4.0.0** | End of Life / Removal | Package officially removed from supported ecosystem releases (breaking change). |

### 3. Canonical Obsolete Signature (v3.0.0)
```csharp
[Obsolete(
    "EricksonLopez.Outbox.MediatR is deprecated and will be removed in v4.0. " +
    "Migrate to EricksonLopez.Outbox.Mediator and EricksonLopez.Mediator.",
    DiagnosticId = "ELMED002",
    UrlFormat = "https://docs.ericksonlopez.dev/migration/{0}")]
```

## Consequences
- **Ecosystem Coherence**: Outbox core and modern adapters remain 100% Native AOT-compliant.
- **Predictable Lifecycle**: Provides existing consumers a predictable timeline to transition to compile-time source-generated mediation.
