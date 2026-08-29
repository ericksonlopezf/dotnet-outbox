// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Outbox.Contracts;

/// <summary>
/// Marks a method as an Inbox consumer (Handler) for a specific event type.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class InboxConsumerAttribute : Attribute
{
    /// <summary>
    /// Gets the event alias that this consumer subscribes to.
    /// </summary>
    public string EventAlias { get; }

    /// <summary>
    /// Gets or sets the optional expiration time in minutes for idempotency records tracked by this consumer.
    /// A value of <c>0</c> or negative indicates the default retention period applies.
    /// </summary>
    public int MaxAgeMinutes { get; set; }

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

    /// <summary>
    /// Initializes a new instance of the <see cref="InboxConsumerAttribute"/> class with a specific idempotency retention max age.
    /// </summary>
    /// <param name="eventAlias">The stable string alias identifying the event type this consumer handles.</param>
    /// <param name="maxAgeMinutes">The maximum retention duration in minutes for deduplication records.</param>
    /// <exception cref="ArgumentException"><paramref name="eventAlias"/> is <see langword="null"/> or white space.</exception>
    public InboxConsumerAttribute(string eventAlias, int maxAgeMinutes)
        : this(eventAlias)
    {
        MaxAgeMinutes = maxAgeMinutes;
    }
}
