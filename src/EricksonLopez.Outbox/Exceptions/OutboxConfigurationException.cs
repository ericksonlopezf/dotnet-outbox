// Copyright © Erickson Lopez. MIT License.

namespace EricksonLopez.Outbox;

/// <summary>
/// Thrown when the startup validator detects missing or misconfigured required services.
/// </summary>
public sealed class OutboxConfigurationException : OutboxException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxConfigurationException"/> class
    /// with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the configuration error.</param>
    public OutboxConfigurationException(string message) : base(message) { }
}
