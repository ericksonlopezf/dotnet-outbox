// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Outbox;

/// <summary>
/// Thrown when the serialization of a message payload fails during the outbox processing.
/// </summary>
public sealed class OutboxSerializationException : OutboxException
{
    /// <summary>
    /// Gets the alias of the message type that failed to serialize.
    /// </summary>
    public string? MessageTypeAlias { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxSerializationException"/> class
    /// with the specified message type alias and the inner exception that caused this exception.
    /// </summary>
    /// <param name="messageTypeAlias">The alias associated with the message type.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public OutboxSerializationException(string messageTypeAlias, Exception innerException)
        : base("Failed to serialize message of type " + messageTypeAlias + ".", innerException)
    {
        MessageTypeAlias = messageTypeAlias;
    }
}
