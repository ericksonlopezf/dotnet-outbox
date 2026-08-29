// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Outbox.Retry;

/// <summary>
/// Represents a retry policy that exponentially backs off between attempts.
/// </summary>
/// <param name="InitialDelay">The initial delay for the first retry attempt.</param>
/// <param name="MaxAttempts">The maximum number of retry attempts.</param>
/// <param name="Factor">The multiplier factor applied to the delay after each attempt.</param>
/// <param name="MaxDelay">The maximum delay allowed. If the calculated delay exceeds this value, this value is used instead.</param>
public sealed record ExponentialBackoffRetryPolicy(
    TimeSpan InitialDelay,
    int MaxAttempts,
    double Factor = 2.0,
    TimeSpan? MaxDelay = null) : RetryPolicy
{
    /// <inheritdoc/>
    public override TimeSpan? GetNextDelay(int currentAttempt)
    {
        if (currentAttempt >= MaxAttempts) return null;

        var delayMs = InitialDelay.TotalMilliseconds * Math.Pow(Factor, currentAttempt - 1);

        if (MaxDelay.HasValue)
        {
            delayMs = Math.Min(delayMs, MaxDelay.Value.TotalMilliseconds);
        }

        return TimeSpan.FromMilliseconds(delayMs);
    }
}
