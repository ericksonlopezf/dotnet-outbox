// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace EricksonLopez.Outbox;

/// <summary>
/// Represents the core entity for a single message stored in the outbox database table.
/// </summary>
/// <remarks>
/// <para>
/// All fields map 1:1 to database columns to enable zero-copy hydration from the DB.
/// </para>
/// <para>
/// <b>Performance note:</b> <c>sealed record</c> (reference type) is used instead of a struct because
/// this type contains multiple fields. As a struct, every parameter pass, foreach iteration,
/// and method return would copy the entire value. As a class, only the reference is copied.
/// Messages are short-lived (Gen0) objects created during DB hydration and collected after dispatch,
/// so GC pressure is minimal compared to the copy elimination savings.
/// </para>
/// </remarks>
/// <param name="Id">The unique identifier of the outbox message.</param>
/// <param name="MessageType">The stable string alias representing the message type.</param>
/// <param name="Payload">The serialized message content.</param>
/// <param name="CorrelationId">The correlation identifier for distributed tracing.</param>
/// <param name="CausationId">The causation identifier for tracking the origin of the message.</param>
/// <param name="Headers">The serialized JSON representation of custom metadata headers.</param>
/// <param name="CreatedAt">The date and time when the message was originally stored in the outbox.</param>
/// <param name="ProcessedAt">The date and time when the message was successfully dispatched to the broker, if applicable.</param>
/// <param name="DeliverAt">The optional future timestamp before which the message must not be dispatched.</param>
/// <param name="Status">The current dispatch status of the message.</param>
/// <param name="RetryCount">
/// The number of times this message has failed and been re-fetched from the database.
/// Note: This is NOT the number of internal retry attempts made by the publisher's RetryPolicy.
/// </param>
/// <param name="Error">The last error message encountered during a failed dispatch attempt, if any.</param>
[DebuggerDisplay("{Id} | {MessageType} | {Status} | Retry={RetryCount}")]
public sealed record OutboxMessage(
    Guid Id,
    string MessageType,
    ReadOnlyMemory<byte> Payload,
    string? CorrelationId,
    string? CausationId,
    ReadOnlyMemory<byte> Headers,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ProcessedAt,
    DateTimeOffset? DeliverAt,
    OutboxMessageStatus Status,
    int RetryCount,
    string? Error)
{
    /// <summary>
    /// Optional metadata extensions for future compatibility and custom routing.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>ROADMAP-v2 — Typed extension values:</b><br/>
    /// The current type <c>IReadOnlyDictionary&lt;string, string&gt;</c> restricts values to strings.
    /// This is sufficient for string-based headers (correlation IDs, trace tags, routing keys),
    /// but precludes typed values such as Kafka partition offsets (<c>long</c>), sequence numbers,
    /// or CDC/WAL metadata. In v2.0, consider changing to
    /// <c>IReadOnlyDictionary&lt;string, object?&gt;</c> or a custom <c>ExtensionMetadata</c> type.
    /// This is a binary breaking change and is deferred to avoid disrupting v1.0 consumers.
    /// </para>
    /// </remarks>
    public IReadOnlyDictionary<string, string>? Extensions { get; init; }

    /// <summary>
    /// Optional tenant identifier for multi-tenant deployments and routing.
    /// </summary>
    public string? TenantId { get; init; }

    /// <inheritdoc/>
    public bool Equals(OutboxMessage? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;

        return Id == other.Id &&
               MessageType == other.MessageType &&
               CorrelationId == other.CorrelationId &&
               CausationId == other.CausationId &&
               CreatedAt == other.CreatedAt &&
               ProcessedAt == other.ProcessedAt &&
               DeliverAt == other.DeliverAt &&
               Status == other.Status &&
               RetryCount == other.RetryCount &&
               Error == other.Error &&
               Payload.Span.SequenceEqual(other.Payload.Span) &&
               Headers.Span.SequenceEqual(other.Headers.Span);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id);
        hash.Add(MessageType);
        hash.Add(CorrelationId);
        hash.Add(CausationId);
        hash.Add(CreatedAt);
        hash.Add(ProcessedAt);
        hash.Add(DeliverAt);
        hash.Add(Status);
        hash.Add(RetryCount);
        hash.Add(Error);
        // Using length as a fast heuristic for memory hash
        hash.Add(Payload.Length);
        hash.Add(Headers.Length);
        return hash.ToHashCode();
    }
}

