; Unshipped analyzer releases for OutboxTypeMappingGenerator
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------

### Changed Rules

Rule ID | New Category | New Severity | Old Category | Old Severity | Notes
--------|--------------|--------------|--------------|--------------|-------
OUTBOXSG003 | Design | Warning | Design | Error | Downgraded from Error to Warning to allow legitimate projects (test utilities, shared contracts libraries, transitive dependencies) to compile without [OutboxMessage] types. Escalate via .editorconfig: dotnet_diagnostic.OUTBOXSG003.severity = error

