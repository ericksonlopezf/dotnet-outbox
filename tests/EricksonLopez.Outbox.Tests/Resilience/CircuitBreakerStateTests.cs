// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using AwesomeAssertions;
using EricksonLopez.Outbox.Retry;
using FsCheck;
using FsCheck.Xunit;
using System.Threading.Tasks;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Resilience;

public sealed class CircuitBreakerStateTests
{
    private sealed class CustomTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;
        public int GetUtcNowCallCount { get; private set; }

        public CustomTimeProvider(DateTimeOffset initialUtcNow)
        {
            _utcNow = initialUtcNow;
        }

        public override DateTimeOffset GetUtcNow()
        {
            GetUtcNowCallCount++;
            return _utcNow;
        }

        public void Advance(TimeSpan delta)
        {
            _utcNow = _utcNow.Add(delta);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-100)]
    public void Constructor_InvalidThreshold_ThrowsArgumentOutOfRangeException(int threshold)
    {
        var act = () => new CircuitBreakerState(threshold);
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("failureThreshold")
            .WithMessage("*Must be > 0.*");
    }

    [Fact]
    public void Constructor_Defaults_InitializesCorrectly()
    {
        var cb = new CircuitBreakerState();

        cb.FailureThreshold.Should().Be(5);
        cb.OpenDuration.Should().Be(TimeSpan.FromSeconds(30));
        cb.State.Should().Be(CircuitState.Closed);
        cb.AllowRequest().Should().BeTrue();
    }

    [Fact]
    public void Constructor_CustomTimeProviderAndDurations_AssignedCorrectly()
    {
        var timeProvider = new CustomTimeProvider(DateTimeOffset.UtcNow);
        var cb = new CircuitBreakerState(3, TimeSpan.FromSeconds(15), timeProvider);

        cb.FailureThreshold.Should().Be(3);
        cb.OpenDuration.Should().Be(TimeSpan.FromSeconds(15));
        cb.State.Should().Be(CircuitState.Closed);
        cb.AllowRequest().Should().BeTrue();
    }

    [Fact]
    public void Constructor_NullOpenDurationAndNullTimeProvider_UsesDefaults()
    {
        var cb = new CircuitBreakerState(4, null, null);

        cb.FailureThreshold.Should().Be(4);
        cb.OpenDuration.Should().Be(TimeSpan.FromSeconds(30));
        cb.State.Should().Be(CircuitState.Closed);
    }

    [Fact]
    public void RecordFailure_IncrementsCount_AndOpensWhenThresholdReached()
    {
        var cb = new CircuitBreakerState(3);

        cb.RecordFailure();
        cb.State.Should().Be(CircuitState.Closed);
        cb.AllowRequest().Should().BeTrue();

        cb.RecordFailure();
        cb.State.Should().Be(CircuitState.Closed);
        cb.AllowRequest().Should().BeTrue();

        cb.RecordFailure();
        cb.State.Should().Be(CircuitState.Open);
        cb.AllowRequest().Should().BeFalse();

        // Additional failure while open stays open
        cb.RecordFailure();
        cb.State.Should().Be(CircuitState.Open);
        cb.AllowRequest().Should().BeFalse();
    }

    [Fact]
    public void RecordSuccess_WhenClosed_ResetsFailureCounter()
    {
        var cb = new CircuitBreakerState(3);

        cb.RecordFailure();
        cb.RecordFailure();
        cb.RecordSuccess();

        // Should need 3 more failures to open
        cb.RecordFailure();
        cb.RecordFailure();
        cb.State.Should().Be(CircuitState.Closed);

        cb.RecordFailure();
        cb.State.Should().Be(CircuitState.Open);
    }

    [Fact]
    public void RecordSuccess_WhenOpen_ClosesCircuitAndResetsCounter()
    {
        var cb = new CircuitBreakerState(2);

        cb.RecordFailure();
        cb.RecordFailure();
        cb.State.Should().Be(CircuitState.Open);

        cb.RecordSuccess();
        cb.State.Should().Be(CircuitState.Closed);
        cb.AllowRequest().Should().BeTrue();
    }

    [Fact]
    public void State_TransitionsToHalfOpen_WhenOpenDurationElapses()
    {
        var initialTime = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new CustomTimeProvider(initialTime);
        var cb = new CircuitBreakerState(2, TimeSpan.FromSeconds(10), timeProvider);

        cb.RecordFailure();
        cb.RecordFailure();
        cb.State.Should().Be(CircuitState.Open);
        cb.AllowRequest().Should().BeFalse();

        // Advance 5 seconds - still open
        timeProvider.Advance(TimeSpan.FromSeconds(5));
        cb.State.Should().Be(CircuitState.Open);
        cb.AllowRequest().Should().BeFalse();

        // Advance another 5 seconds - elapsed 10 seconds => transitions to HalfOpen
        timeProvider.Advance(TimeSpan.FromSeconds(5));
        cb.State.Should().Be(CircuitState.HalfOpen);
        cb.AllowRequest().Should().BeTrue();
    }

    [Fact]
    public void HalfOpen_Success_ClosesCircuit()
    {
        var initialTime = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new CustomTimeProvider(initialTime);
        var cb = new CircuitBreakerState(2, TimeSpan.FromSeconds(10), timeProvider);

        cb.RecordFailure();
        cb.RecordFailure();
        timeProvider.Advance(TimeSpan.FromSeconds(10));
        cb.State.Should().Be(CircuitState.HalfOpen);

        cb.RecordSuccess();
        cb.State.Should().Be(CircuitState.Closed);
        cb.AllowRequest().Should().BeTrue();

        // Needs full threshold again
        cb.RecordFailure();
        cb.State.Should().Be(CircuitState.Closed);
        cb.RecordFailure();
        cb.State.Should().Be(CircuitState.Open);
    }

    [Fact]
    public void HalfOpen_SingleFailure_ImmediatelyReopensCircuit()
    {
        var initialTime = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);
        var timeProvider = new CustomTimeProvider(initialTime);
        var cb = new CircuitBreakerState(5, TimeSpan.FromSeconds(10), timeProvider);

        for (int i = 0; i < 5; i++)
            cb.RecordFailure();

        cb.State.Should().Be(CircuitState.Open);

        timeProvider.Advance(TimeSpan.FromSeconds(10));
        cb.State.Should().Be(CircuitState.HalfOpen);

        // A single failure in HalfOpen immediately re-opens the circuit
        int callsBefore = timeProvider.GetUtcNowCallCount;
        cb.RecordFailure();
        int callsAfter = timeProvider.GetUtcNowCallCount;
        (callsAfter - callsBefore).Should().Be(1);

        cb.State.Should().Be(CircuitState.Open);
        cb.AllowRequest().Should().BeFalse();
    }

    [Fact]
    public void Concurrency_ThreadSafety_MaintainsConsistency()
    {
        var cb = new CircuitBreakerState(10, TimeSpan.FromMilliseconds(50));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        Parallel.For(0, 100, index =>
        {
            for (int i = 0; i < 500 && !cts.IsCancellationRequested; i++)
            {
                cb.RecordFailure();
                cb.AllowRequest();
                cb.RecordSuccess();
                _ = cb.State;
            }
        });

        // After stopping, operations still work without deadlock or corruption
        cb.RecordSuccess();
        cb.State.Should().Be(CircuitState.Closed);
    }

    [Property]
    public bool CircuitBreaker_Transitions_Correctly(PositiveInt failureThresholdGen, bool[] requestResults)
    {
        int failureThreshold = failureThresholdGen.Item;
        if (failureThreshold <= 0 || requestResults == null || requestResults.Length == 0)
        {
            return true;
        }

        var cb = new CircuitBreakerState(failureThreshold, TimeSpan.FromDays(1));
        int consecutiveFailures = 0;

        foreach (var isSuccess in requestResults)
        {
            if (cb.State == CircuitState.Open)
            {
                // Timeout is 1 day, so it stays Open
                if (cb.AllowRequest()) return false;
            }
            else if (cb.State == CircuitState.Closed)
            {
                if (isSuccess)
                {
                    cb.RecordSuccess();
                    consecutiveFailures = 0;
                }
                else
                {
                    cb.RecordFailure();
                    consecutiveFailures++;
                    if (consecutiveFailures >= failureThreshold && cb.State != CircuitState.Open)
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }
}


