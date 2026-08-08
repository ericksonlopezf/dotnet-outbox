// Stryker disable String : Exception messages are not tested for exact matching
using System;
using System.Diagnostics;

namespace EricksonLopez.Outbox;

/// <summary>
/// Represents the result of a single message dispatch attempt through an <see cref="IBrokerPublisher"/>.
/// </summary>
/// <remarks>
/// <para><b>Valid state combinations:</b></para>
/// <list type="table">
///   <listheader><term>Success</term><term>ShouldRetry</term><term>Meaning</term></listheader>
///   <item><term>true</term><term>false</term><term>Published successfully.</term></item>
///   <item><term>false</term><term>true</term><term>Transient failure — schedule retry with exponential backoff.</term></item>
///   <item><term>false</term><term>false</term><term>Fatal failure — dead-letter the message, do not retry.</term></item>
/// </list>
/// <para>
/// <b>IBrokerPublisher contract guidance:</b><br/>
/// Return <see cref="FailAndRetry(Exception)"/> for <b>recoverable</b> errors (network timeout, broker unavailable, rate-limited).<br/>
/// Return <see cref="FailFatal(Exception)"/> for <b>unrecoverable</b> errors (serialization failure, schema mismatch, message too large for broker).
/// </para>
/// </remarks>
/// <param name="Success">Indicates whether the message was successfully dispatched.</param>
/// <param name="ShouldRetry">Indicates whether the dispatch operation should be retried after a failure.</param>
/// <param name="Error">The exception that caused the failure, if any; otherwise, <see langword="null"/>.</param>
/// <param name="IncrementRetryCount">Indicates whether the dispatcher should increment the retry counter for the message.</param>
[DebuggerDisplay("Success={Success} ShouldRetry={ShouldRetry} IncrementRetry={IncrementRetryCount}")]
public readonly record struct DispatchResult(
    bool Success,
    bool ShouldRetry,
    Exception? Error,
    bool IncrementRetryCount)
{
    /// <summary>
    /// Creates a successful dispatch result.
    /// </summary>
    /// <returns>A <see cref="DispatchResult"/> representing a successful dispatch.</returns>
    public static DispatchResult Ok() => new(true, false, null, false);

    /// <summary>
    /// Creates a transient failure result that will trigger an exponential-backoff retry.
    /// </summary>
    /// <remarks>
    /// Use this for recoverable errors such as network timeouts, broker unavailability, or rate limiting.
    /// </remarks>
    /// <param name="ex">The exception representing the transient failure.</param>
    /// <returns>A <see cref="DispatchResult"/> representing a transient failure.</returns>
    public static DispatchResult FailAndRetry(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        return new(false, true, ex, true);
    }

    /// <summary>
    /// Creates a transient failure result that will trigger an exponential-backoff retry.
    /// </summary>
    /// <param name="ex">The exception representing the transient failure.</param>
    /// <param name="incrementRetryCount">Indicates whether to increment the message's retry counter.</param>
    /// <returns>A <see cref="DispatchResult"/> representing a transient failure.</returns>
    public static DispatchResult FailAndRetry(Exception ex, bool incrementRetryCount)
    {
        ArgumentNullException.ThrowIfNull(ex);
        return new(false, true, ex, incrementRetryCount);
    }

    /// <summary>
    /// Creates a fatal failure result that will dead-letter the message without further retries.
    /// </summary>
    /// <remarks>
    /// Use this for unrecoverable errors such as serialization failure, schema mismatch, or an oversized payload.
    /// </remarks>
    /// <param name="ex">The exception representing the fatal failure.</param>
    /// <returns>A <see cref="DispatchResult"/> representing a fatal failure.</returns>
    public static DispatchResult FailFatal(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        return new(false, false, ex, false);
    }

    /// <summary>
    /// Creates a fatal failure result that will dead-letter the message without further retries.
    /// </summary>
    /// <param name="messageId">The identifier of the message that failed.</param>
    /// <param name="retryCount">The current retry count of the message.</param>
    /// <param name="reason">The string message explaining the fatal failure.</param>
    /// <returns>A <see cref="DispatchResult"/> representing a fatal failure.</returns>
    public static DispatchResult FailFatal(Guid messageId, int retryCount, string reason)
    {
        ArgumentNullException.ThrowIfNull(reason);
        return new DispatchResult(false, false, new OutboxDispatchException(messageId, retryCount, reason), false);
    }

    /// <summary>
    /// Creates a fatal failure result with a reason, indicating that the message should not be retried.
    /// </summary>
    /// <param name="reason">The reason for the fatal failure.</param>
    /// <returns>A <see cref="DispatchResult"/> representing a fatal failure.</returns>
    public static DispatchResult FailFatal(string reason) =>
        FailFatal(Guid.Empty, 0, reason);

    /// <summary>
    /// Validates the state of the dispatch result and throws an exception if the state is logically invalid.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the result is in an incoherent state, such as <c>Success=true</c> and <c>ShouldRetry=true</c>.
    /// </exception>
    public void ThrowIfInvalid()
    {
        if (Success && ShouldRetry)
        {
            throw new InvalidOperationException(
                "DispatchResult is in an invalid state: Success=true and ShouldRetry=true are mutually exclusive. " +
                "A successful dispatch should never request a retry. Use DispatchResult.Ok() or DispatchResult.FailAndRetry().");
        }
        
        if (!Success && Error is null)
        {
            throw new InvalidOperationException("Failed DispatchResult must have an Error attached to it.");
        }
    }
}
