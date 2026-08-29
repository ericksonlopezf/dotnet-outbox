// Copyright © Erickson Lopez. MIT License.
using System;
using System.Linq;

namespace EricksonLopez.Outbox.EntityFrameworkCore.Entities;

/// <summary>
/// Represents an Entity Framework Core database entity for a dead letter message.
/// </summary>
public class DeadLetterMessageEntity
{
    /// <summary>Gets or sets the dead letter identifier.</summary>
    public Guid Id { get; set; }
    
    /// <summary>Gets or sets the original message identifier.</summary>
    public Guid OriginalMessageId { get; set; }
    
    /// <summary>Gets or sets the message type.</summary>
    public string MessageType { get; set; } = string.Empty;
    
    /// <summary>Gets or sets the message payload.</summary>
    public byte[] Payload { get; set; } = Array.Empty<byte>();
    
    /// <summary>Gets or sets the correlation identifier.</summary>
    public string? CorrelationId { get; set; }
    
    /// <summary>Gets or sets the causation identifier.</summary>
    public string? CausationId { get; set; }
    
    /// <summary>Gets or sets the headers in JSON format.</summary>
    public string HeadersJson { get; set; } = "{}";
    
    /// <summary>Gets or sets the timestamp when the original message was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }
    
    /// <summary>Gets or sets the timestamp when the message was moved to the dead letter queue.</summary>
    public DateTimeOffset DeadLetteredAt { get; set; }
    
    /// <summary>Gets or sets the number of retry attempts.</summary>
    public int RetryCount { get; set; }
    
    /// <summary>Gets or sets the reason the message was dead lettered.</summary>
    public string Reason { get; set; } = string.Empty;
    
    /// <summary>Gets or sets the last error encountered during processing.</summary>
    public string? LastError { get; set; }

    /// <summary>
    /// Converts the entity into the domain <see cref="DeadLetterMessage"/> model.
    /// </summary>
    /// <returns>A new <see cref="DeadLetterMessage"/> instance.</returns>
    public DeadLetterMessage ToModel() => new(
        Id, OriginalMessageId, MessageType, Payload, CorrelationId, CausationId, System.Text.Encoding.UTF8.GetBytes(HeadersJson ?? "{}"), CreatedAt, DeadLetteredAt, RetryCount, Reason, LastError);

    /// <summary>
    /// Creates a new <see cref="DeadLetterMessageEntity"/> from the domain <see cref="DeadLetterMessage"/> model.
    /// </summary>
    /// <param name="model">The domain model to convert.</param>
    /// <returns>A new <see cref="DeadLetterMessageEntity"/> instance.</returns>
    public static DeadLetterMessageEntity FromModel(DeadLetterMessage model) => new()
    {
        Id = model.Id,
        OriginalMessageId = model.OriginalMessageId,
        MessageType = model.MessageType,
        Payload = model.Payload.ToArray(),
        CorrelationId = model.CorrelationId,
        CausationId = model.CausationId,
        HeadersJson = System.Text.Encoding.UTF8.GetString(model.Headers.Span),
        CreatedAt = model.CreatedAt,
        DeadLetteredAt = model.DeadLetteredAt,
        RetryCount = model.RetryCount,
        Reason = model.Reason,
        LastError = model.LastError
    };
}


