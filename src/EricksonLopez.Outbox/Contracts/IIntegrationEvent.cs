using System;

namespace EricksonLopez.Outbox.Contracts;

/// <summary>
/// Serves as an optional marker interface for integration events.
/// </summary>
/// <remarks>
/// Although the library primarily relies on attributes like <see cref="OutboxMessageAttribute"/>, 
/// implementing this interface can help standardize contracts in strict Domain-Driven Design (DDD) environments.
/// </remarks>
public interface IIntegrationEvent
{
    /// <summary>
    /// Gets the globally unique identifier for the event.
    /// </summary>
    /// <remarks>
    /// If this value is not provided or if the type does not implement this interface, the Outbox infrastructure 
    /// will automatically generate one (UUIDv7 is recommended).
    /// </remarks>
    Guid EventId { get; }
    
    /// <summary>
    /// Gets the exact date and time the domain event occurred.
    /// </summary>
    DateTimeOffset OccurredOn { get; }
}
