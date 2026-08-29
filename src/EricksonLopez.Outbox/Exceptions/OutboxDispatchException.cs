// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Outbox;

/// <summary>
/// Thrown when a dispatch operation fails fatally after exhausting all retry policies.
/// </summary>
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
