; Copyright © Erickson Lopez. MIT License.
; Shipped analyzer releases for OutboxTypeMappingGenerator
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 1.0.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
OUTBOXSG001 | Design | Error | Duplicate outbox message alias — two types share the same [OutboxMessage("alias")] value. Aliases must be unique within the assembly.
OUTBOXSG002 | Design | Error | Open generic type cannot be used as an outbox message directly. Use a closed generic or a concrete wrapper.
OUTBOXSG003 | Design | Error | No types decorated with [OutboxMessage] found — the generated type resolver resolves nothing, causing runtime failures. Suppress with <NoWarn>OUTBOXSG003</NoWarn> if manual registration via options.UseTypeResolver() is used.
