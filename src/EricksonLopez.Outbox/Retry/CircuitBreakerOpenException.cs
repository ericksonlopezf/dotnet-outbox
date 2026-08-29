// Copyright © Erickson Lopez. MIT License.
using System;
using EricksonLopez.Result;

namespace EricksonLopez.Outbox.Retry;

/// <summary>
/// Thrown when a request is rejected because the <see cref="CircuitBreakerState"/> is in the Open state.
/// This exception is used internally by <see cref="RetryDispatcherInterceptor"/> to signal that
/// the broker is unreachable and the circuit breaker is preventing further attempts.
/// </summary>
/// <remarks>
/// <para>
/// This exception is never propagated to the caller. The <see cref="RetryDispatcherInterceptor"/>
/// converts circuit-breaker rejections into <c>DispatchResult.FailAndRetry(ex, incrementRetryCount: false)</c>,
/// which causes the message to remain in-flight and be reclaimed by the stale message reclaimer
/// after the configured <c>ReclaimTimeout</c> \u2014 acting as a natural backoff without burning retry attempts.
/// </para>
/// </remarks>
public sealed class CircuitBreakerOpenException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CircuitBreakerOpenException"/> class.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public CircuitBreakerOpenException(string message) : base(message) { }
}


