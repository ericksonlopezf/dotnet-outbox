// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Outbox.Retry;

/// <summary>
/// Provides a retry policy that exponentially increases the delay between attempts.
/// This prevents overwhelming a recovering network broker.
/// </summary>
public sealed class ExponentialBackoffPolicy : IRetryPolicy
{
    private readonly int _maxAttempts;
    private readonly TimeSpan _initialDelay;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExponentialBackoffPolicy"/> class.
    /// </summary>
    /// <param name="maxAttempts">The maximum number of retry attempts.</param>
    /// <param name="initialDelayMs">The initial delay in milliseconds before the first retry.</param>
    public ExponentialBackoffPolicy(int maxAttempts = 10, int initialDelayMs = 100)
    {
        _maxAttempts = maxAttempts;
        _initialDelay = TimeSpan.FromMilliseconds(initialDelayMs);
    }

    /// <inheritdoc/>
    public TimeSpan GetNextDelay(int currentAttempt)
    {
        return TimeSpan.FromMilliseconds(_initialDelay.TotalMilliseconds * Math.Pow(2, currentAttempt - 1));
    }

    /// <inheritdoc/>
    public bool ShouldRetry(int currentAttempt, Exception exception)
    {
        return currentAttempt < _maxAttempts;
    }
}

