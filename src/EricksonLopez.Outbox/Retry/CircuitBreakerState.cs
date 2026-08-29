// Copyright © Erickson Lopez. MIT License.
using System;
using System.Diagnostics;
using System.Threading;

namespace EricksonLopez.Outbox.Retry;


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
    private readonly TimeProvider _timeProvider;
    private int _failureCount;
    private int _state;
    private long _openedAtTicks;
    private readonly object _syncRoot = new();

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
        : this(failureThreshold, openDuration, TimeProvider.System)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CircuitBreakerState"/> class with a custom <see cref="TimeProvider"/>.
    /// </summary>
    /// <param name="failureThreshold">The number of consecutive failures before opening the circuit.</param>
    /// <param name="openDuration">The duration the circuit stays open. Defaults to 30 seconds.</param>
    /// <param name="timeProvider">The time provider to use for time measurements.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="failureThreshold"/> is less than or equal to 0.</exception>
    public CircuitBreakerState(int failureThreshold, TimeSpan? openDuration, TimeProvider? timeProvider)
    {
        if (failureThreshold <= 0)
            throw new ArgumentOutOfRangeException(nameof(failureThreshold), "Must be > 0.");

        FailureThreshold = failureThreshold;
        OpenDuration = openDuration ?? TimeSpan.FromSeconds(30);
        _timeProvider = timeProvider ?? TimeProvider.System;
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
                        if (_timeProvider.GetUtcNow() - openedAt >= OpenDuration)
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
                _openedAtTicks = _timeProvider.GetUtcNow().UtcTicks;
                _state = (int)CircuitState.Open;
                _failureCount = FailureThreshold;
                return;
            }

            _failureCount++;
            if (_failureCount >= FailureThreshold)
            {
                _openedAtTicks = _timeProvider.GetUtcNow().UtcTicks;
                _state = (int)CircuitState.Open;
            }
        }
    }
}

