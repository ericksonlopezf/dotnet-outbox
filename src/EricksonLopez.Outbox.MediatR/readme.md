<!-- Copyright © Erickson Lopez. MIT License. -->

# EricksonLopez.Outbox.MediatR

> **Warning: Legacy Transitional Bridge & Non-AOT Package**  
> This package is provided strictly as a transitional adapter for legacy codebases using MediatR with `EricksonLopez.Outbox`.

## Overview
`EricksonLopez.Outbox.MediatR` integrates `MediatR` notification publishing with the transactional Outbox pipeline.

## AOT & Trimming Compatibility Notice
- **Native AOT Compatible**: `No` (`<IsAotCompatible>false</IsAotCompatible>`)
- **Trimmable**: `No` (`<IsTrimmable>false</IsTrimmable>`)
- **Technical Rationale**: Third-party `MediatR` depends on runtime reflection and dynamic type activation.

## Migration & Deprecation Roadmap
1. **Current Status**: Maintenance mode for legacy compatibility.
2. **Recommended Target**: Migrate to [`EricksonLopez.Outbox.Mediator`](https://github.com/ericksonlopezf/dotnet-outbox) paired with [`EricksonLopez.Mediator`](https://github.com/ericksonlopezf/dotnet-mediator) for zero-allocation, source-generated, Native AOT-first dispatching.
3. **Deprecation Plan**: This package will be marked as `[Obsolete]` in upcoming major releases once full ecosystem migration to `EricksonLopez.Mediator` is finalized.
