using System;

namespace EricksonLopez.Outbox;

/// <summary>
/// Represents a message that failed to dispatch and is pending retry.
/// </summary>
/// <remarks>
/// Once the retry count exceeds the policy maximum, it graduates to a <see cref="DeadLetterMessage"/>.
/// </remarks>
/// <param name="Id">The unique identifier of the failed message record.</param>
/// <param name="OriginalMessageId">The unique identifier of the original outbox message.</param>
/// <param name="MessageType">The stable string alias representing the message type.</param>
/// <param name="Payload">The serialized message content.</param>
/// <param name="CorrelationId">The correlation identifier for distributed tracing.</param>
/// <param name="CausationId">The causation identifier for tracking the origin of the message.</param>
/// <param name="Headers">The serialized JSON representation of custom metadata headers.</param>
/// <param name="CreatedAt">The date and time when the original message was stored in the outbox.</param>
/// <param name="FailedAt">The date and time when the most recent dispatch attempt failed.</param>
/// <param name="Error">The exception message or stack trace associated with the failure.</param>
/// <param name="RetryCount">The number of times this message has failed dispatch.</param>
/// <param name="NextRetryAt">The optional future timestamp indicating when the next retry attempt should occur.</param>
public readonly record struct FailedMessage(
    Guid Id,
    Guid OriginalMessageId,
    string MessageType,
    ReadOnlyMemory<byte> Payload,
    string? CorrelationId,
    string? CausationId,
    ReadOnlyMemory<byte> Headers,
    DateTimeOffset CreatedAt,
    DateTimeOffset FailedAt,
    string? Error,
    int RetryCount,
    DateTimeOffset? NextRetryAt)
{
    /// <summary>
    /// Creates a <see cref="FailedMessage"/> from an <see cref="OutboxMessage"/> after a dispatch failure.
    /// </summary>
    /// <param name="original">The original outbox message that failed.</param>
    /// <param name="retryCount">The current retry attempt number.</param>
    /// <param name="error">The exception message or stack trace associated with the failure.</param>
    /// <param name="retryAfter">The optional time interval to wait before the next retry attempt.</param>
    /// <returns>A new <see cref="FailedMessage"/> instance representing the pending retry state.</returns>
    public static FailedMessage FromOutboxMessage(
        OutboxMessage original,
        int retryCount,
        string? error,
        TimeSpan? retryAfter = null)
    {
        return new FailedMessage(
#if NET9_0_OR_GREATER
            Id: Guid.CreateVersion7(),
#else
            Id: Guid.NewGuid(),
#endif
            OriginalMessageId: original.Id,
            MessageType: original.MessageType,
            Payload: original.Payload,
            CorrelationId: original.CorrelationId,
            CausationId: original.CausationId,
            Headers: original.Headers,
            CreatedAt: original.CreatedAt,
            FailedAt: DateTimeOffset.UtcNow,
            Error: error,
            RetryCount: retryCount,
            NextRetryAt: retryAfter.HasValue
                ? DateTimeOffset.UtcNow.Add(retryAfter.Value)
                : null);
    }
}
