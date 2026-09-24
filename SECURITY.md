<!-- Copyright © Erickson Lopez. MIT License. -->

# Security Policy

At `EricksonLopez.Outbox`, security and data integrity are our highest priorities. This library is designed to process business-critical financial and transactional data, so we treat every vulnerability report with extreme urgency.

## Supported Versions

We follow Semantic Versioning. Security updates are actively backported to supported release lines.

| Version | Supported          | Security Patch SLA |
| ------- | ------------------ | ------------------ |
| 1.0.x   | :white_check_mark: | 72 Hours           |
| < 1.0   | :x:                | None (Pre-release) |

## Reporting a Vulnerability

**DO NOT** report security vulnerabilities via public GitHub Issues. 

If you discover a vulnerability, please email **ericksonlopezf@gmail.com**.
You can expect a response within **24 hours**.

Please include the following information in your report:
- The version of `EricksonLopez.Outbox` you are using.
- The storage engine and broker you are using.
- A clear description of the vulnerability.
- Steps to reproduce (a proof of concept is highly appreciated).

### Disclosure Process
1. We will acknowledge receipt of your vulnerability report within 24 hours.
2. We will investigate the issue and determine its severity.
3. If confirmed, we will issue a private patch and share it with you for verification.
4. We will publish a CVE and release a public patch across all supported versions.
5. We will publicly credit you for the discovery (unless you prefer to remain anonymous).

## Supply Chain Security Guarantee

We take active measures to secure our supply chain against malicious actors:
- **OIDC Publishing**: Our GitHub Actions pipeline publishes to NuGet via OpenID Connect (`NuGet/login@v1`). No static API keys are stored in our repository.
- **Sigstore Provenance**: All `.nupkg` and `.snupkg` files are signed with Sigstore Provenance Attestations (`actions/attest-build-provenance@v2`).
- **Strong Name Signing**: All compiled assemblies (`.dll`) are signed with the `EricksonLopez.snk` key.
- **Dependency Pinning**: We use Central Package Management (`Directory.Packages.props`) and Dependabot to monitor and aggressively patch upstream vulnerabilities.
- **NuGet Audit**: Enabled with `NuGetAuditMode=all` and `NuGetAuditLevel=low` — any CVE in any dependency (including transitive) fails the build.

## Known Security Boundaries

`EricksonLopez.Outbox` explicitly does **not** protect against:
- **SQL Injection via custom payloads**: If you deserialize user-input directly into an outbox payload without sanitization, you are responsible for the validation.
- **Broker Authorization**: The library assumes that the connection string provided in `options.UseRabbitMq(connString)` has the proper ACLs to publish to the specified topics. We do not manage broker-level authentication.
- **Sensitive Data in Error Logs**: The library persists dispatch exceptions to the `error` column in the database (truncated to 4000 characters). If your broker publisher or middleware throws exceptions containing sensitive data (e.g., connection strings in stack traces), use `IErrorSanitizer` (see [docs/error-sanitization.md](docs/error-sanitization.md)) to sanitize exception details before writing to the Dead Letter Queue.

