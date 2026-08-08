using System;

namespace EricksonLopez.Outbox;

/// <summary>
/// Represents a message that has exhausted all retry attempts and cannot be processed.
/// </summary>
/// <remarks>
/// Dead-lettered messages are stored in a separate table or queue for manual inspection, replay, or discard.
/// </remarks>
/// <param name="Id">The unique identifier of the dead-letter record.</param>
/// <param name="OriginalMessageId">The unique identifier of the original outbox message.</param>
/// <param name="MessageType">The stable string alias representing the message type.</param>
/// <param name="Payload">The serialized message content.</param>
/// <param name="CorrelationId">The correlation identifier for distributed tracing.</param>
/// <param name="CausationId">The causation identifier for tracking the origin of the message.</param>
/// <param name="Headers">The serialized JSON representation of custom metadata headers.</param>
/// <param name="CreatedAt">The date and time when the original message was stored in the outbox.</param>
/// <param name="DeadLetteredAt">The date and time when the message was moved to the dead-letter queue.</param>
/// <param name="RetryCount">The total number of retry attempts made before the message was dead-lettered.</param>
/// <param name="Reason">A brief description or code indicating the reason for dead-lettering.</param>
/// <param name="LastError">The full text or stack trace of the last error encountered during dispatch.</param>
public readonly record struct DeadLetterMessage(
    Guid Id,
    Guid OriginalMessageId,
    string MessageType,
    ReadOnlyMemory<byte> Payload,
    string? CorrelationId,
    string? CausationId,
    ReadOnlyMemory<byte> Headers,
    DateTimeOffset CreatedAt,
    DateTimeOffset DeadLetteredAt,
    int RetryCount,
    string Reason,
    string? LastError)
{
    /// <summary>
    /// Creates a <see cref="DeadLetterMessage"/> from an <see cref="OutboxMessage"/> that
    /// has exhausted its configured number of retry attempts.
    /// </summary>
    /// <param name="original">The original outbox message that failed.</param>
    /// <param name="retryCount">The total number of retry attempts made.</param>
    /// <param name="reason">A brief description or reason code for the failure.</param>
    /// <param name="lastError">The full text or stack trace of the last error encountered.</param>
    /// <returns>A new <see cref="DeadLetterMessage"/> instance representing the failed message.</returns>
    public static DeadLetterMessage FromOutboxMessage(
        OutboxMessage original,
        int retryCount,
        string reason,
        string? lastError = null)
    {
        return new DeadLetterMessage(
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
            DeadLetteredAt: DateTimeOffset.UtcNow,
            RetryCount: retryCount,
            Reason: reason,
            LastError: lastError);
    }
}
