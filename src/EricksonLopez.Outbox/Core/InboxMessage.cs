// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Outbox;

/// <summary>
/// Represents a message received from a message broker and stored in the Inbox for idempotent processing.
/// </summary>
/// <param name="Id">The unique identifier of the inbox message.</param>
/// <param name="MessageType">The stable string alias representing the message type.</param>
/// <param name="Payload">The serialized message content.</param>
/// <param name="CorrelationId">The correlation identifier for distributed tracing.</param>
/// <param name="CausationId">The causation identifier for tracking the origin of the message.</param>
/// <param name="Headers">The serialized JSON representation of custom metadata headers.</param>
/// <param name="ReceivedAt">The date and time when the message was received and stored in the inbox.</param>
/// <param name="ProcessedAt">The date and time when the message was successfully processed by the consumer, if applicable.</param>
/// <param name="Status">The current processing status of the inbox message.</param>
/// <param name="Error">The last error message encountered during a failed processing attempt, if any.</param>
public readonly record struct InboxMessage(
    Guid Id,
    string MessageType,
    ReadOnlyMemory<byte> Payload,
    string? CorrelationId,
    string? CausationId,
    ReadOnlyMemory<byte> Headers,
    DateTimeOffset ReceivedAt,
    DateTimeOffset? ProcessedAt,
    int Status,
    string? Error);

