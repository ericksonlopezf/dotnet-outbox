using System;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Outbox.Retry;

/// <summary>
/// A retry policy that applies exponential backoff with random jitter to prevent the
/// thundering-herd problem when multiple dispatcher instances retry concurrently after a shared failure.
/// </summary>
/// <param name="InitialDelay">The base delay for the first retry attempt, before the exponential factor is applied.</param>
/// <param name="MaxAttempts">The maximum number of retry attempts before the policy stops retrying.</param>
/// <param name="Factor">The multiplier applied to the delay after each consecutive attempt. Defaults to <c>2.0</c> (doubles each attempt).</param>
/// <param name="MaxDelay">The upper bound on the calculated delay. If <see langword="null"/>, no cap is applied.</param>
/// <param name="JitterFactor">The fraction of the base delay to use as the jitter window, distributed as ±<c>JitterFactor</c> of the base. Defaults to <c>0.25</c>.</param>
public sealed record JitterRetryPolicy(
    TimeSpan InitialDelay,
    int MaxAttempts,
    double Factor = 2.0,
    TimeSpan? MaxDelay = null,
    double JitterFactor = 0.25) : RetryPolicy
{
    // Thread-safe random for jitter calculation
    private static readonly Random _rng = Random.Shared;

    /// <inheritdoc/>
    public override TimeSpan? GetNextDelay(int currentAttempt)
    {
        if (currentAttempt >= MaxAttempts) return null;

        // Exponential base delay
        var baseMs = InitialDelay.TotalMilliseconds * Math.Pow(Factor, currentAttempt - 1);

        // Stryker disable all : Math and floating point equality inside jitter calculations are notoriously brittle to test
        if (MaxDelay.HasValue && baseMs > MaxDelay.Value.TotalMilliseconds)
            baseMs = MaxDelay.Value.TotalMilliseconds;

        // Add Â±JitterFactor random deviation to the base
        var jitterMs = baseMs * JitterFactor * (2.0 * _rng.NextDouble() - 1.0);
        var total = Math.Max(0, baseMs + jitterMs);

        return TimeSpan.FromMilliseconds(total);
        // Stryker restore all
    }
}
