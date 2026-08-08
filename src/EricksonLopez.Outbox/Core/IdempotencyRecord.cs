using System;

namespace EricksonLopez.Outbox;

/// <summary>
/// Represents a record used to ensure idempotent processing of messages within the Inbox.
/// </summary>
/// <param name="MessageId">The unique identifier of the processed message.</param>
/// <param name="ConsumerId">The unique identifier of the consumer that processed the message.</param>
/// <param name="ProcessedAt">The exact date and time the message was successfully processed.</param>
public readonly record struct IdempotencyRecord(
    string MessageId,
    string ConsumerId,
    DateTimeOffset ProcessedAt);
