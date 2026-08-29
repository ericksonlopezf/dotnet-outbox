// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Outbox.EntityFrameworkCore.Entities;

/// <summary>
/// Represents an Entity Framework Core database entity for an inbox idempotency record.
/// </summary>
public class IdempotencyRecordEntity
{
    /// <summary>Gets or sets the message identifier.</summary>
    public string MessageId { get; set; } = string.Empty;
    
    /// <summary>Gets or sets the consumer identifier.</summary>
    public string ConsumerId { get; set; } = string.Empty;
    
    /// <summary>Gets or sets the timestamp when the message was processed.</summary>
    public DateTimeOffset ProcessedAt { get; set; }

    /// <summary>
    /// Converts the entity into the domain <see cref="IdempotencyRecord"/> model.
    /// </summary>
    /// <returns>A new <see cref="IdempotencyRecord"/> instance.</returns>
    public IdempotencyRecord ToModel() => new(MessageId, ConsumerId, ProcessedAt);

    /// <summary>
    /// Creates a new <see cref="IdempotencyRecordEntity"/> from the domain <see cref="IdempotencyRecord"/> model.
    /// </summary>
    /// <param name="model">The domain model to convert.</param>
    /// <returns>A new <see cref="IdempotencyRecordEntity"/> instance.</returns>
    public static IdempotencyRecordEntity FromModel(IdempotencyRecord model) => new()
    {
        MessageId = model.MessageId,
        ConsumerId = model.ConsumerId,
        ProcessedAt = model.ProcessedAt
    };
}

