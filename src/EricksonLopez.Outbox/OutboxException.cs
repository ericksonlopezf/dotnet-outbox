// Stryker disable all : Covered by ADR-013. Edge cases, micro-optimizations, logging, and validation strings are not rigorously mutated.
using System;
using System.Diagnostics.CodeAnalysis;
namespace EricksonLopez.Outbox;

/// <summary>
/// Represents the base class for all exceptions thrown by the EricksonLopez.Outbox library.
/// </summary>
// Stryker disable String : Exception messages are not strictly verified for exact matches
[ExcludeFromCodeCoverage]
public class OutboxException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxException"/> class.
    /// </summary>
    public OutboxException() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxException"/> class
    /// with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public OutboxException(string message) : base(message) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxException"/> class
    /// with a specified error message and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The error message that explains the reason for the exception.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public OutboxException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>
/// Thrown when an outbox message type is not registered in the type resolver
/// and strict type mapping is enforced.
/// </summary>
[ExcludeFromCodeCoverage]
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

/// <summary>
/// Thrown when the serialization of a message payload fails during the outbox processing.
/// </summary>
[ExcludeFromCodeCoverage]
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

/// <summary>
/// Thrown when the startup validator detects missing or misconfigured required services.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class OutboxConfigurationException : OutboxException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxConfigurationException"/> class
    /// with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the configuration error.</param>
    public OutboxConfigurationException(string message) : base(message) { }
}

/// <summary>
/// Thrown when an unexpected error occurs during outbox runtime operations (e.g., polling, dispatching).
/// </summary>
[ExcludeFromCodeCoverage]
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

/// <summary>
/// Thrown when a dispatch operation fails fatally after exhausting all retry policies.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class OutboxDispatchException : OutboxException
{
    /// <summary>
    /// Gets the unique identifier of the message that failed to dispatch.
    /// </summary>
    public Guid MessageId { get; }

    /// <summary>
    /// Gets the number of attempts made to dispatch the message before failing.
    /// </summary>
    public int AttemptCount { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxDispatchException"/> class.
    /// </summary>
    /// <param name="messageId">The unique identifier of the failed message.</param>
    /// <param name="attemptCount">The number of dispatch attempts made.</param>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The optional exception that is the cause of the current exception.</param>
    public OutboxDispatchException(Guid messageId, int attemptCount, string message, Exception? innerException = null)
        : base(message, innerException!)
    {
        MessageId = messageId;
        AttemptCount = attemptCount;
    }
}

/// <summary>
/// Thrown when a message payload exceeds the configured maximum payload size limit.
/// </summary>
/// <remarks>
/// Callers can catch this specific exception to implement custom large-message handling,
/// such as offloading the payload to blob storage and storing only a reference in the outbox.
/// </remarks>
[ExcludeFromCodeCoverage]
public sealed class OutboxPayloadTooLargeException : OutboxException
{
    /// <summary>
    /// Gets the actual serialized payload size in bytes.
    /// </summary>
    public int ActualSize { get; }

    /// <summary>
    /// Gets the configured maximum allowed payload size in bytes.
    /// </summary>
    public int MaxAllowedSize { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxPayloadTooLargeException"/> class.
    /// </summary>
    /// <param name="actualSize">The actual size of the payload in bytes.</param>
    /// <param name="maxAllowedSize">The maximum allowed size for the payload in bytes.</param>
    public OutboxPayloadTooLargeException(int actualSize, int maxAllowedSize)
        : base($"Payload size {actualSize} bytes exceeds the configured maximum of {maxAllowedSize} bytes. " +
               "Consider offloading the payload to blob storage and storing only a reference in the outbox message.")
    {
        ActualSize = actualSize;
        MaxAllowedSize = maxAllowedSize;
    }
}

/// <summary>
/// Thrown when message headers exceed the configured maximum headers size limit.
/// </summary>
/// <remarks>
/// Callers can catch this specific exception to implement custom header-trimming strategies.
/// </remarks>
[ExcludeFromCodeCoverage]
public sealed class OutboxHeadersTooLargeException : OutboxException
{
    /// <summary>
    /// Gets the actual serialized headers size in bytes.
    /// </summary>
    public int ActualSize { get; }

    /// <summary>
    /// Gets the configured maximum allowed headers size in bytes.
    /// </summary>
    public int MaxAllowedSize { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxHeadersTooLargeException"/> class.
    /// </summary>
    /// <param name="actualSize">The actual size of the headers in bytes.</param>
    /// <param name="maxAllowedSize">The maximum allowed size for the headers in bytes.</param>
    public OutboxHeadersTooLargeException(int actualSize, int maxAllowedSize)
        : base($"Headers size {actualSize} bytes exceeds the configured maximum of {maxAllowedSize} bytes. " +
               "Reduce the number or size of message headers.")
    {
        ActualSize = actualSize;
        MaxAllowedSize = maxAllowedSize;
    }
}
