// Copyright © Erickson Lopez. MIT License.
using System;
using EricksonLopez.Result;

namespace EricksonLopez.Outbox.Diagnostics;


/// <summary>
/// Provides a mechanism to sanitize exceptions before they are logged or stored in the database.
/// Useful for masking sensitive information (e.g., PII, passwords, connection strings) that might leak in raw exception messages.
/// </summary>
public interface IErrorSanitizer
{
    /// <summary>
    /// Sanitizes the given exception into a safe string representation.
    /// </summary>
    /// <param name="exception">The exception to sanitize.</param>
    /// <returns>A sanitized string representation of the error.</returns>
    string Sanitize(Exception exception);
}



