namespace EricksonLopez.Outbox.Diagnostics;

using System;

/// <summary>
/// A default implementation of <see cref="IErrorSanitizer"/> that returns the raw exception message.
/// </summary>
public sealed class DefaultErrorSanitizer : IErrorSanitizer
{
    /// <inheritdoc/>
    public string Sanitize(Exception exception)
    {
        return exception.Message;
    }
}
