// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Outbox.Retry;

/// <summary>
/// Represents a retry policy that applies exponential backoff with random jitter to prevent the
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
    private readonly Func<double> _randomDoubleProvider = () => Random.Shared.NextDouble();

    internal JitterRetryPolicy(
        TimeSpan initialDelay,
        int maxAttempts,
        double factor,
        TimeSpan? maxDelay,
        double jitterFactor,
        Func<double> randomDoubleProvider)
        : this(initialDelay, maxAttempts, factor, maxDelay, jitterFactor)
    {
        _randomDoubleProvider = randomDoubleProvider ?? (() => Random.Shared.NextDouble());
    }

    /// <inheritdoc/>
    public override TimeSpan? GetNextDelay(int currentAttempt)
    {
        if (currentAttempt >= MaxAttempts) return null;

        // Exponential base delay
        var baseMs = InitialDelay.TotalMilliseconds * Math.Pow(Factor, currentAttempt - 1);

        if (MaxDelay.HasValue)
        {
            baseMs = Math.Min(baseMs, MaxDelay.Value.TotalMilliseconds);
        }

        // Add ±JitterFactor random deviation to the base: (2.0 * nextDouble - 1.0) is in [-1.0, 1.0]
        var jitterMs = baseMs * JitterFactor * (2.0 * _randomDoubleProvider() - 1.0);
        var total = Math.Max(0, baseMs + jitterMs);

        return TimeSpan.FromMilliseconds(total);
    }
}


