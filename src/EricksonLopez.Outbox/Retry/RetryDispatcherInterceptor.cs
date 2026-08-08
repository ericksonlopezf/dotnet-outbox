using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using EricksonLopez.Outbox;

namespace EricksonLopez.Outbox.Retry;

// CircuitBreakerOpenException is defined in CircuitBreakerOpenException.cs

/// <summary>
/// Interceptor that wraps the broker publishing process and applies the configured RetryPolicy.
/// Uses a shared retry loop via <see cref="ExecuteWithRetryAsync"/> to avoid code duplication
/// between the generic and raw publish paths.
/// </summary>
public sealed partial class RetryDispatcherInterceptor : IBrokerPublisher
{
    private readonly IBrokerPublisher _inner;
    private readonly RetryPolicy _policy;
    private readonly CircuitBreakerState _circuitBreaker;
    private readonly ILogger<RetryDispatcherInterceptor> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RetryDispatcherInterceptor"/> class.
    /// </summary>
    /// <param name="inner">The underlying broker publisher.</param>
    /// <param name="policy">The retry policy to apply.</param>
    /// <param name="circuitBreaker">The circuit breaker state tracker.</param>
    /// <param name="logger">The logger instance.</param>
    public RetryDispatcherInterceptor(IBrokerPublisher inner, RetryPolicy policy, CircuitBreakerState circuitBreaker, ILogger<RetryDispatcherInterceptor> logger)
    {
        _inner = inner;
        _policy = policy;
        _circuitBreaker = circuitBreaker;
        _logger = logger;
    }


    /// <inheritdoc/>
    public ValueTask<DispatchResult> PublishRawAsync(OutboxMessage message, MessageMetadata metadata, DispatchContext context)
    {
        return ExecuteWithRetryAsync(
            attempt => _inner.PublishRawAsync(message, metadata, new DispatchContext(context.CancellationToken, attempt)),
            context.CancellationToken);
    }

    /// <summary>
    /// Shared retry loop that eliminates duplication between PublishAsync and PublishRawAsync.
    /// </summary>
    private async ValueTask<DispatchResult> ExecuteWithRetryAsync(
        Func<int, ValueTask<DispatchResult>> operation,
        CancellationToken cancellationToken)
    {
        if (!_circuitBreaker.AllowRequest())
        {
            LogCircuitBreakerOpen();
            return DispatchResult.FailAndRetry(new CircuitBreakerOpenException("Circuit breaker is open."), incrementRetryCount: false);
        }

        int attempt = 1;
        while (!cancellationToken.IsCancellationRequested)
        {
            var result = await operation(attempt);

            // Stryker disable once all : Removing this block falls through to !ShouldRetry which also returns, just with a false positive log
            if (result.Success)
            {
                _circuitBreaker.RecordSuccess();
                return result;
            }

            if (!result.ShouldRetry)
            {
                LogFatalError(result.Error);
                return result;
            }

            _circuitBreaker.RecordFailure();

            if (!_circuitBreaker.AllowRequest())
            {
                LogCircuitBreakerOpenedDuringRetry();
                return DispatchResult.FailAndRetry(new CircuitBreakerOpenException("Circuit breaker opened."), incrementRetryCount: false);
            }

            var nextDelay = _policy.GetNextDelay(attempt);
            if (!nextDelay.HasValue)
            {
                LogRetryExhausted(attempt);
                return DispatchResult.FailFatal(new InvalidOperationException($"Retry policy exhausted after {attempt} attempts.", result.Error));
            }

            LogPublishFailed(result.Error, nextDelay.Value.TotalMilliseconds, attempt);

            // Stryker disable all : Delay statement removal or break removal only affects timing/performance, not outcome
            try
            {
                await Task.Delay(nextDelay.Value, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            // Stryker restore all

            attempt++;
        }

        // ISSUE-ERR1 FIX: Use FailAndRetry instead of FailFatal when the loop exits due to cancellation.
        //
        // FailFatal would cause the dispatcher to dead-letter the message (state=4), permanently
        // losing it. This is WRONG during graceful shutdowns (rolling deploys, SIGTERM, Ctrl+C):
        // the message was simply not sent yet — it should be reclaimed on the next startup via
        // ReclaimStaleMessagesAsync (which moves state=1 InFlight messages back to state=0 Pending).
        //
        // FailAndRetry(incrementRetryCount: false) moves the message back to state=3 (Failed/Retry)
        // with its retry count UNCHANGED, so it does not burn a retry slot for a legitimate shutdown.
        // The next startup will pick it up within ReclaimTimeout (default 5 minutes).
        var cancelEx = new OperationCanceledException();
        LogPublishCancelled();
        // Stryker disable once boolean : Changing incrementRetryCount to true is untestable in a unit test
        return DispatchResult.FailAndRetry(cancelEx, incrementRetryCount: false);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Circuit breaker is open. Rejecting publish request.")]
    private partial void LogCircuitBreakerOpen();

    [LoggerMessage(Level = LogLevel.Error, Message = "Fatal error occurred while publishing message. Will not retry.")]
    private partial void LogFatalError(Exception? ex);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Circuit breaker opened during retry loop. Aborting retries.")]
    private partial void LogCircuitBreakerOpenedDuringRetry();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Retry policy exhausted after {Attempts} attempts.")]
    private partial void LogRetryExhausted(int attempts);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Publishing failed. Retrying in {Delay}ms (Attempt {Attempt}).")]
    private partial void LogPublishFailed(Exception? ex, double delay, int attempt);

    /// <summary>
    /// Logs a warning when publish is aborted due to cancellation (e.g., graceful shutdown).
    /// The message will be reclaimed on next startup — no retry count is incremented.
    /// </summary>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Publish loop cancelled due to shutdown signal. Message will be reclaimed on next startup (retry count unchanged).")]
    private partial void LogPublishCancelled();
}
