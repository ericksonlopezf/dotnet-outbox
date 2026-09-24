<!-- Copyright © Erickson Lopez. MIT License. -->

# Operational Security: Dead-Letter Queue & Error Sanitization

When exceptions occur during message dispatch (e.g., database connection drops, broker authentication failures, payload validation errors), failure details are persisted to the Dead-Letter Queue (DLQ) table to assist operational troubleshooting.

However, raw exception stack traces and messages frequently contain sensitive information, such as:
- Connection strings with embedded passwords (`User ID=...;Password=...`).
- Secret keys and API tokens.
- Personally Identifiable Information (PII) embedded in validation exception messages.

To prevent sensitive data leaks into DLQ storage and observability tools, `EricksonLopez.Outbox` provides the `IErrorSanitizer` abstraction.

---

## 1. The `IErrorSanitizer` Abstraction

```csharp
namespace EricksonLopez.Outbox.Diagnostics;

public interface IErrorSanitizer
{
    string Sanitize(Exception exception);
}
```

The default implementation (`DefaultErrorSanitizer`) uses compile-time C# source-generated regular expressions (`[GeneratedRegex]`) to strip known connection string patterns (`password=...`, `secret=...`, `api_key=...`) and HTTP bearer tokens from exception messages before persisting to `IDeadLetterRepository`. Because regexes are compiled at build time, it introduces zero runtime Reflection and is fully NativeAOT compatible.

---

## 2. Implementing a Custom `IErrorSanitizer`

You can customize redaction rules for domain-specific secrets or regulatory compliance:

```csharp
using System.Text.RegularExpressions;
using EricksonLopez.Outbox.Diagnostics;

public sealed partial class ComplianceErrorSanitizer : IErrorSanitizer
{
    [GeneratedRegex(@"\b(?:\d[ -]*?){13,16}\b", RegexOptions.Compiled)]
    private static partial Regex CreditCardRegex();

    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9\-\._~\+\/]+=*", RegexOptions.Compiled)]
    private static partial Regex SecretTokenRegex();

    public string Sanitize(Exception exception)
    {
        if (exception == null) return string.Empty;

        var fullDetails = $"{exception.GetType().FullName}: {exception.Message}\n{exception.StackTrace}";

        // Redact Credit Cards and Bearer Tokens
        var sanitized = CreditCardRegex().Replace(fullDetails, "[REDACTED_CC]");
        sanitized = SecretTokenRegex().Replace(sanitized, "Bearer [REDACTED_TOKEN]");

        return sanitized;
    }
}
```

---

## 3. Registration

Register your custom sanitizer during service configuration:

```csharp
services.AddOutboxServices(builder =>
{
    builder.Services.AddSingleton<IErrorSanitizer, ComplianceErrorSanitizer>();
});
```

---

## 4. Best Practices for DLQ Management

1. **Never Log Unsanitized Exceptions to DLQ**: Ensure all custom dispatcher interceptors pass exceptions through `IErrorSanitizer`.
2. **Review DLQ Retention Policies**: Use `OutboxCleanupService` with configured `MaxMessageAge` to purge processed and stale DLQ records periodically.
3. **Card-Holder & GDPR Compliance**: Sanitizing DLQ records ensures compliance with PCI-DSS Requirement 3 and GDPR Article 32.
