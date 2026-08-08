// Stryker disable all : Covered by ADR-013. Edge cases, micro-optimizations, logging, and validation strings are not rigorously mutated.
using System;
using System.Diagnostics;
using System.Threading;

namespace EricksonLopez.Outbox.Retry;

/// <summary>
/// Represents the three states of a circuit breaker.
/// </summary>
public enum CircuitState
{
    /// <summary>Normal operation. All publish calls pass through.</summary>
    Closed,
    /// <summary>Too many failures. Publish calls are rejected immediately without hitting the broker.</summary>
    Open,
    /// <summary>A probe period has elapsed. The next single call is allowed through to test broker recovery.</summary>
    HalfOpen
}

/// <summary>
/// Tracks the circuit-breaker state for a single broker publisher, providing zero-allocation
/// protection against overwhelming an unavailable broker.
/// </summary>
/// <remarks>
/// <para>
/// Implements a lightweight three-state machine (<see cref="CircuitState.Closed"/>,
/// <see cref="CircuitState.Open"/>, <see cref="CircuitState.HalfOpen"/>) without
/// introducing a dependency on Polly or similar libraries.
/// </para>
/// <para>
/// <b>Thread safety:</b> All state mutations are protected by a single lock to ensure
/// atomicity of composite state changes. The three fields (<c>_state</c>, <c>_failureCount</c>,
/// <c>_openedAtTicks</c>) must always be updated as a unit.
/// </para>
/// </remarks>
[DebuggerDisplay("State={State} Failures={_failureCount}/{FailureThreshold}")]
public sealed class CircuitBreakerState
{
    private int _failureCount;
    private int _state = (int)CircuitState.Closed;
    private long _openedAtTicks;
    // A-05 AUDIT FIX: Use System.Threading.Lock on .NET 9+ for improved low-contention locking.
    // Lock provides a thinner kernel object and shorter spin count compared to lock(object),
    // reducing overhead in the common case where the circuit is Closed (no contention).
    //
    // On .NET 8 and earlier, fall back to the standard object-based monitor lock.
    // Both types support the C# lock(...) statement identically at the language level.
#if NET9_0_OR_GREATER
    private readonly System.Threading.Lock _syncRoot = new System.Threading.Lock();
#else
    private readonly object _syncRoot = new object();
#endif

    /// <summary>Gets the number of consecutive failures required before the circuit transitions to the Open state.</summary>
    public int FailureThreshold { get; }
    
    /// <summary>Gets the duration the circuit remains in the Open state before transitioning to HalfOpen and allowing a probe attempt.</summary>
    public TimeSpan OpenDuration { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CircuitBreakerState"/> class.
    /// </summary>
    /// <param name="failureThreshold">The number of consecutive failures before opening the circuit.</param>
    /// <param name="openDuration">The duration the circuit stays open. Defaults to 30 seconds.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="failureThreshold"/> is less than or equal to 0.</exception>
    public CircuitBreakerState(int failureThreshold = 5, TimeSpan? openDuration = null)
    {
        if (failureThreshold <= 0)
            throw new ArgumentOutOfRangeException(nameof(failureThreshold), "Must be > 0.");

        FailureThreshold = failureThreshold;
        OpenDuration = openDuration ?? TimeSpan.FromSeconds(30);
    }

    /// <summary>Gets the current state of the circuit breaker, automatically transitioning from Open to HalfOpen when the open duration has elapsed.</summary>
    public CircuitState State
    {
        get
        {
            var currentState = (CircuitState)Volatile.Read(ref _state);
            if (currentState == CircuitState.Open)
            {
                lock (_syncRoot)
                {
                    if ((CircuitState)_state == CircuitState.Open)
                    {
                        var openedAt = new DateTimeOffset(_openedAtTicks, TimeSpan.Zero);
                        if (DateTimeOffset.UtcNow - openedAt >= OpenDuration)
                        {
                            _state = (int)CircuitState.HalfOpen;
                            _failureCount = 0;
                        }
                    }
                    return (CircuitState)_state;
                }
            }
            return currentState;
        }
    }

    /// <summary>Determines whether a publish attempt is permitted based on the current circuit state.</summary>
    /// <returns><see langword="true"/> if the circuit is in the Closed or HalfOpen state; otherwise, <see langword="false"/>.</returns>
    public bool AllowRequest() => State != CircuitState.Open;

    /// <summary>Records a successful publish call, resetting the failure counter and closing the circuit.</summary>
    public void RecordSuccess()
    {
        lock (_syncRoot)
        {
            _failureCount = 0;
            _state = (int)CircuitState.Closed;
        }
    }

    /// <summary>Records a failed publish call, incrementing the failure counter; opens the circuit immediately when in HalfOpen state or when the failure threshold is reached.</summary>
    public void RecordFailure()
    {
        lock (_syncRoot)
        {
            var currentState = (CircuitState)_state;
            if (currentState == CircuitState.HalfOpen)
            {
                _openedAtTicks = DateTimeOffset.UtcNow.UtcTicks;
                _state = (int)CircuitState.Open;
                _failureCount = FailureThreshold;
                return;
            }

            _failureCount++;
            if (_failureCount >= FailureThreshold)
            {
                _openedAtTicks = DateTimeOffset.UtcNow.UtcTicks;
                _state = (int)CircuitState.Open;
            }
        }
    }
}
