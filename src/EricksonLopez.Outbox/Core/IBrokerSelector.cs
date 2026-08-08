using System;

namespace EricksonLopez.Outbox;

/// <summary>
/// Selects the appropriate <see cref="IBrokerPublisher"/> for a given outbox message.
/// </summary>
public interface IBrokerSelector
{
    /// <summary>
    /// Gets the publisher for the specified message.
    /// </summary>
    /// <param name="message">The outbox message being dispatched.</param>
    /// <returns>The <see cref="IBrokerPublisher"/> responsible for publishing the message.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no suitable publisher is found.</exception>
    IBrokerPublisher GetPublisher(OutboxMessage message);
}
