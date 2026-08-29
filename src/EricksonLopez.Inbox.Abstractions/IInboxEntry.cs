// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Inbox;

/// <summary>
/// Represents an entry in the consumer inbox for deduplication and idempotency tracking.
/// </summary>
public interface IInboxEntry
{
    /// <summary>
    /// Gets the unique incoming message or event identifier.
    /// </summary>
    string MessageId { get; }

    /// <summary>
    /// Gets the logical name of the consumer or handler processing the message.
    /// </summary>
    string ConsumerName { get; }

    /// <summary>
    /// Gets the timestamp when the message was processed.
    /// </summary>
    DateTimeOffset ProcessedAt { get; }
}
