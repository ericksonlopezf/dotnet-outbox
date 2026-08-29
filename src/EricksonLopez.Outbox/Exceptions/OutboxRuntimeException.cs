// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Outbox;

/// <summary>
/// Thrown when an unexpected error occurs during outbox runtime operations (e.g., polling, dispatching).
/// </summary>
public sealed class OutboxRuntimeException : OutboxException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxRuntimeException"/> class
    /// with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the runtime error.</param>
    public OutboxRuntimeException(string message) : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxRuntimeException"/> class
    /// with a specified error message and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The message that describes the runtime error.</param>
    /// <param name="innerException">The inner exception.</param>
    public OutboxRuntimeException(string message, Exception innerException) : base(message, innerException) { }
}
