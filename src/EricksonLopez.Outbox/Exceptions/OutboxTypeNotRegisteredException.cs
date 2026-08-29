// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Outbox;

/// <summary>
/// Thrown when an outbox message type is not registered in the type resolver
/// and strict type mapping is enforced.
/// </summary>
public sealed class OutboxTypeNotRegisteredException : OutboxException
{
    /// <summary>
    /// Gets the type of the outbox message that failed to resolve.
    /// </summary>
    public Type MessageType { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxTypeNotRegisteredException"/> class
    /// for the specified message type.
    /// </summary>
    /// <param name="messageType">The type of the message that was not registered.</param>
    public OutboxTypeNotRegisteredException(Type messageType)
        : base("Type " + messageType.FullName + " is not registered in the OutboxMessageTypeResolver. " +
               "Decorate the type with [OutboxMessage(alias)] and register it during startup.")
    {
        MessageType = messageType;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxTypeNotRegisteredException"/> class
    /// for the specified message type with a reference to the inner exception that caused this exception.
    /// </summary>
    /// <param name="messageType">The type of the message that was not registered.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public OutboxTypeNotRegisteredException(Type messageType, Exception innerException)
        : base("Type " + messageType.FullName + " is not registered in the OutboxMessageTypeResolver.", innerException)
    {
        MessageType = messageType;
    }
}
