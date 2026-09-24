// Copyright © Erickson Lopez. MIT License.
using System;
using System.Text.RegularExpressions;

namespace EricksonLopez.Outbox.Diagnostics;

/// <summary>
/// Provides the default implementation of <see cref="IErrorSanitizer"/> that strips known
/// sensitive patterns (passwords, connection strings, bearer tokens) from exception messages.
/// </summary>
public sealed partial class DefaultErrorSanitizer : IErrorSanitizer
{
    [GeneratedRegex(@"(?i)(password|pwd|secret|token|api[_-]?key)\s*=\s*[^;,\r\n]+", RegexOptions.Compiled)]
    private static partial Regex ConnectionStringSecretRegex();

    [GeneratedRegex(@"(?i)bearer\s+(?:token\s+)?[A-Za-z0-9\-\._~\+\/=]+", RegexOptions.Compiled)]
    private static partial Regex BearerTokenRegex();

    /// <inheritdoc/>
    public string Sanitize(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        var message = exception.Message;

        if (string.IsNullOrEmpty(message))
            return string.Empty;

        // Redact connection string secrets: password=***REDACTED***;
        message = ConnectionStringSecretRegex().Replace(message, "$1=***REDACTED***");

        // Redact bearer tokens: Bearer ***REDACTED***
        message = BearerTokenRegex().Replace(message, "Bearer ***REDACTED***");

        return message;
    }
}



