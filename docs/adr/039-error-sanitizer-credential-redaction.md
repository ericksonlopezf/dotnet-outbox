<!-- Copyright © Erickson Lopez. MIT License. -->

# ADR-039: Error Sanitizer Credential and Token Redaction

## Status
Accepted — September 2026

## Date
2026-09-10

## Context

When message dispatch attempts encounter errors (such as network disconnections, broker authentication rejections, or database write failures), exception details are recorded in dead-letter storage (`IDeadLetterRepository`) and dispatched to logging sinks and OpenTelemetry exception events.

However, raw database exceptions (e.g., from `NpgsqlException`, `SqlException`, `MySqlException`, `OracleException`) and broker client exceptions frequently include connection strings, authentication credentials, or HTTP Authorization bearer tokens in their exception messages and stack traces:
- `password=...`, `pwd=...`, `secret=...`, `token=...`, `api_key=...`
- `Bearer eyJhbGciOi...`

Persisting or emitting unredacted exception messages introduces severe operational security risks, violating PCI-DSS Requirement 3 and GDPR Article 32 regarding data protection at rest and in telemetry pipelines.

## Decision

Introduce automated credential redaction into the default error sanitization pipeline:

1. **`IErrorSanitizer` Interface**: Resides in `EricksonLopez.Outbox.Diagnostics` with a single contract method:
   ```csharp
   public interface IErrorSanitizer
   {
       string Sanitize(Exception exception);
   }
   ```
2. **`DefaultErrorSanitizer` Implementation**: Provides out-of-the-box credential stripping using C# source-generated regular expressions (`[GeneratedRegex]`):
   - Redacts connection string secrets: `(?i)(password|pwd|secret|token|api[_-]?key)\s*=\s*[^;,\r\n]+` -> `$1=***REDACTED***`
   - Redacts bearer tokens: `(?i)bearer\s+(?:token\s+)?[A-Za-z0-9\-\._~\+\/=]+` -> `Bearer ***REDACTED***`
3. **NativeAOT Compatibility**: Compiling regexes at build time with Roslyn source generators avoids runtime reflection and dynamic IL generation, maintaining full NativeAOT compatibility.
4. **Extensibility**: Users requiring domain-specific redaction (e.g., credit card numbers, national IDs) can substitute `IErrorSanitizer` in the dependency injection container via `builder.Services.AddSingleton<IErrorSanitizer, CustomSanitizer>()`.

## Rationale

- **Security by Default**: Exception strings are sanitized before dead-letter persistence without requiring manual intervention or third-party middleware.
- **Zero Runtime Reflection**: Source-generated regexes emit state machines at compile time, ensuring zero reflection overhead on exception handling paths.
- **Fail-Safe Processing**: Sanitization handles null inputs and empty messages gracefully.

## Consequences

### Positive
- Prevents database passwords, API tokens, and secret credentials from leaking into dead-letter tables, application logs, and telemetry collectors.
- Complies with enterprise security, PCI-DSS, and GDPR audits.
- Compatible with NativeAOT ahead-of-time compilation.

### Negative
- Regular expression substitution incurs minimal CPU cycles on exception handling paths (failure scenarios only, not on normal publishing hot paths).

## Related ADRs

- [ADR-004](004-roslyn-source-generators-aot.md) — Roslyn Source Generators for AOT
- [ADR-029](029-dead-letter-repository-transaction-boundary.md) — DeadLetterRepository Standalone Transaction Boundary
