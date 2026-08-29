// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Outbox;

/// <summary>
/// Represents the current processing status of an outbox message, as stored in the database.
/// </summary>
public enum OutboxMessageStatus
{
    /// <summary>
    /// The message is queued and ready to be dispatched.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// The message has been claimed by a dispatcher and is currently being processed.
    /// This state prevents duplicate processing across concurrent dispatcher instances.
    /// </summary>
    InFlight = 1,

    /// <summary>
    /// The message was successfully dispatched to the broker.
    /// </summary>
    Dispatched = 2,

    /// <summary>
    /// The message failed to dispatch and is scheduled for retry.
    /// </summary>
    Failed = 3,

    /// <summary>
    /// The message has exhausted all retry attempts and has been moved to the dead-letter queue.
    /// </summary>
    DeadLettered = 4
}
