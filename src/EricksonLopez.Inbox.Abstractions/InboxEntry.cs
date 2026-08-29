// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Inbox;

/// <summary>
/// Represents an immutable record implementing <see cref="IInboxEntry"/> for message deduplication and idempotency tracking.
/// </summary>
/// <param name="MessageId">The unique incoming message or event identifier.</param>
/// <param name="ConsumerName">The logical name of the consumer or handler processing the message.</param>
/// <param name="ProcessedAt">The timestamp when the message was processed.</param>
public sealed record InboxEntry(
    string MessageId,
    string ConsumerName,
    DateTimeOffset ProcessedAt) : IInboxEntry
{
    /// <summary>
    /// Gets a value indicating whether this entry has no message identifier and consumer name.
    /// </summary>
    public bool IsEmpty => MessageId.Length == 0 && ConsumerName.Length == 0;
}
