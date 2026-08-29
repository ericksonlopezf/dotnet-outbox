// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Outbox.Diagnostics;


/// <summary>
/// Provides the default implementation of <see cref="IErrorSanitizer"/> that returns the raw exception message.
/// </summary>
public sealed class DefaultErrorSanitizer : IErrorSanitizer
{
    /// <inheritdoc/>
    public string Sanitize(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return exception.Message;
    }
}


