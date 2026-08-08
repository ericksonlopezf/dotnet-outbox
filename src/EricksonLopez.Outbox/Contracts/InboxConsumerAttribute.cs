// Stryker disable String : Exception messages are not tested for exact matching
using System;

namespace EricksonLopez.Outbox.Contracts;

/// <summary>
/// Marks a method as an Inbox consumer (Handler) for a specific event type.
/// </summary>
/// <remarks>
/// The Source Generator will wrap the annotated method with idempotency checks and transaction management.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class InboxConsumerAttribute : Attribute
{
    /// <summary>
    /// Gets the event alias that this consumer subscribes to.
    /// </summary>
    public string EventAlias { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="InboxConsumerAttribute"/> class.
    /// </summary>
    /// <param name="eventAlias">The stable string alias identifying the event type this consumer handles.</param>
    /// <exception cref="ArgumentException"><paramref name="eventAlias"/> is <see langword="null"/> or white space.</exception>
    public InboxConsumerAttribute(string eventAlias)
    {
        if (string.IsNullOrWhiteSpace(eventAlias))
            throw new ArgumentException("The event alias cannot be null or empty.", nameof(eventAlias));

        EventAlias = eventAlias;
    }
}
