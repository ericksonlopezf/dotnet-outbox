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

/// <summary>
/// A retry policy that uses a fixed delay between attempts.
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

/// <summary>
/// A retry policy that exponentially backs off between attempts.
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
        
        var delay = TimeSpan.FromMilliseconds(InitialDelay.TotalMilliseconds * Math.Pow(Factor, currentAttempt - 1));
        
        // Stryker disable once Equality : Delay exact equality is brittle to unit test
        if (MaxDelay.HasValue && delay > MaxDelay.Value)
        {
            return MaxDelay.Value;
        }

        return delay;
    }
}
