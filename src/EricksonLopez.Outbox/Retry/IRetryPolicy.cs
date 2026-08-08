using System;

namespace EricksonLopez.Outbox.Retry;

/// <summary>
/// Defines a policy for retrying failed dispatch operations.
/// </summary>
public interface IRetryPolicy
{
    /// <summary>
    /// Calculates the delay before the next retry attempt.
    /// </summary>
    /// <param name="currentAttempt">The number of retry attempts made so far.</param>
    /// <returns>The time interval to wait before making the next attempt.</returns>
    TimeSpan GetNextDelay(int currentAttempt);

    /// <summary>
    /// Determines whether another retry should be attempted based on the current attempt count and the exception.
    /// </summary>
    /// <param name="currentAttempt">The number of retry attempts made so far.</param>
    /// <param name="exception">The exception that caused the current attempt to fail.</param>
    /// <returns><see langword="true"/> if the operation should be retried; otherwise, <see langword="false"/>.</returns>
    bool ShouldRetry(int currentAttempt, Exception exception);
}
