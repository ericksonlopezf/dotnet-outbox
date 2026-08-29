// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Outbox.Retry;

/// <summary>
/// Defines the base contract for a retry policy used when publishing messages.
/// </summary>
public abstract record RetryPolicy
{
    /// <summary>
    /// Gets the default retry policy: exponential backoff starting at 1 second,
    /// capped at 30 seconds, with a maximum of 5 attempts.
    /// </summary>
    /// <remarks>Suitable for most transient broker failure scenarios.</remarks>
    public static RetryPolicy Default { get; } = new ExponentialBackoffRetryPolicy(
        InitialDelay: TimeSpan.FromSeconds(1),
        MaxAttempts: 5,
        Factor: 2.0,
        MaxDelay: TimeSpan.FromSeconds(30));

    /// <summary>
    /// Gets the delay before the next retry attempt, or <see langword="null"/> if retries should stop.
    /// </summary>
    /// <param name="currentAttempt">The current retry attempt count.</param>
    /// <returns>The <see cref="TimeSpan"/> to wait, or <see langword="null"/> to stop retrying.</returns>
    public abstract TimeSpan? GetNextDelay(int currentAttempt);
}

