using System;

namespace EricksonLopez.Outbox.EntityFrameworkCore.Entities;

/// <summary>
/// Entity Framework Core reference entity representing an outbox message.
/// </summary>
public class OutboxMessageEntity
{
    /// <summary>Gets or sets the message identifier.</summary>
    public Guid Id { get; set; }
    
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
    
    /// <summary>Gets or sets the timestamp when the message was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }
    
    /// <summary>Gets or sets the timestamp when the message was processed.</summary>
    public DateTimeOffset? ProcessedAt { get; set; }
    
    /// <summary>Gets or sets the timestamp when the message should be delivered.</summary>
    public DateTimeOffset? DeliverAt { get; set; }
    
    /// <summary>Gets or sets the state of the message.</summary>
    public int State { get; set; }
    
    /// <summary>Gets or sets the number of retry attempts.</summary>
    public int RetryCount { get; set; }
    
    /// <summary>Gets or sets the error encountered during processing.</summary>
    public string? Error { get; set; }

    /// <summary>
    /// Converts the entity into the domain <see cref="OutboxMessage"/> model.
    /// </summary>
    /// <returns>A new <see cref="OutboxMessage"/> instance.</returns>
    public OutboxMessage ToModel() => new(
        Id, MessageType, Payload, CorrelationId, CausationId, System.Text.Encoding.UTF8.GetBytes(HeadersJson ?? "{}"), CreatedAt, ProcessedAt, DeliverAt, (EricksonLopez.Outbox.OutboxMessageStatus)State, RetryCount, Error);

    /// <summary>
    /// Creates a new <see cref="OutboxMessageEntity"/> from the domain <see cref="OutboxMessage"/> model.
    /// </summary>
    /// <param name="model">The domain model to convert.</param>
    /// <returns>A new <see cref="OutboxMessageEntity"/> instance.</returns>
    public static OutboxMessageEntity FromModel(OutboxMessage model) => new()
    {
        Id = model.Id,
        MessageType = model.MessageType,
        Payload = model.Payload.ToArray(),
        CorrelationId = model.CorrelationId,
        CausationId = model.CausationId,
        HeadersJson = System.Text.Encoding.UTF8.GetString(model.Headers.Span),
        CreatedAt = model.CreatedAt,
        ProcessedAt = model.ProcessedAt,
        DeliverAt = model.DeliverAt,
        State = (int)model.Status,
        RetryCount = model.RetryCount,
        Error = model.Error
    };
}
