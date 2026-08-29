// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Outbox.Retry;

/// <summary>
/// Represents a retry policy that uses a fixed delay between attempts.
/// </summary>
/// <param name="Delay">The fixed delay to apply between attempts.</param>
/// <param name="MaxAttempts">The maximum number of retry attempts.</param>
public sealed record FixedDelayRetryPolicy(TimeSpan Delay, int MaxAttempts) : RetryPolicy
{
    /// <inheritdoc/>
    public override TimeSpan? GetNextDelay(int currentAttempt)
    {
        if (currentAttempt >= MaxAttempts) return null;
        return Delay;
    }
}
