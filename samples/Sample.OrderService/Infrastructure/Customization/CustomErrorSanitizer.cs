// Copyright © Erickson Lopez. MIT License.
using System;
using EricksonLopez.Outbox.Diagnostics;

namespace Sample.OrderService.Infrastructure.Customization;

/// <summary>
/// Showcase Error Sanitizer
/// Demonstrates how an <see cref="IErrorSanitizer"/> cleans exceptions before logging them
/// to avoid leaking PII (Personally Identifiable Information) or connection strings.
/// </summary>
public sealed class CustomErrorSanitizer : IErrorSanitizer
{
    /// <inheritdoc/>
    public string Sanitize(Exception exception)
    {
        // Example: redact password from SQL exceptions or similar
        if (exception.Message.Contains("Password=", StringComparison.OrdinalIgnoreCase))
        {
            // Note: In real apps, you'd probably return a generic exception message 
            // or use Regex to specifically target the password value.
            return "A database error occurred (details redacted for security).";
        }

        return exception.ToString();
    }
}
