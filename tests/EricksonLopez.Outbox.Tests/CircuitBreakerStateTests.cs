using System;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox.Retry;
using Xunit;

namespace EricksonLopez.Outbox.Tests;

public class CircuitBreakerStateTests
{
    [Fact]
    public void Constructor_Should_Throw_On_Invalid_Threshold()
    {
        Action act = () => _ = new CircuitBreakerState(0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Default_State_Should_Be_Closed()
    {
        var cb = new CircuitBreakerState();
        cb.State.Should().Be(CircuitState.Closed);
        cb.AllowRequest().Should().BeTrue();
    }

    [Fact]
    public void RecordFailure_Should_Open_Circuit_When_Threshold_Reached()
    {
        var cb = new CircuitBreakerState(2);
        
        cb.RecordFailure();
        cb.State.Should().Be(CircuitState.Closed);
        cb.AllowRequest().Should().BeTrue();
        
        cb.RecordFailure(); // 2nd failure
        cb.State.Should().Be(CircuitState.Open);
        cb.AllowRequest().Should().BeFalse();
        
        // Additional failures should keep it open
        cb.RecordFailure();
        cb.State.Should().Be(CircuitState.Open);
    }

    [Fact]
    public void RecordSuccess_Should_Reset_Failure_Count()
    {
        var cb = new CircuitBreakerState(2);
        
        cb.RecordFailure();
        cb.RecordSuccess();
        cb.RecordFailure();
        
        cb.State.Should().Be(CircuitState.Closed);
    }

    [Fact]
    public async Task Circuit_Should_Transition_To_HalfOpen_After_Duration()
    {
        var cb = new CircuitBreakerState(1, TimeSpan.FromMilliseconds(50));
        
        cb.RecordFailure();
        cb.State.Should().Be(CircuitState.Open);
        
        await Task.Delay(100);
        
        cb.State.Should().Be(CircuitState.HalfOpen);
        cb.AllowRequest().Should().BeTrue();
    }

    [Fact]
    public async Task Success_In_HalfOpen_Should_Close_Circuit()
    {
        var cb = new CircuitBreakerState(1, TimeSpan.FromMilliseconds(50));
        
        cb.RecordFailure();
        await Task.Delay(100);
        
        cb.State.Should().Be(CircuitState.HalfOpen);
        
        cb.RecordSuccess();
        cb.State.Should().Be(CircuitState.Closed);
    }

    [Fact]
    public async Task Failure_In_HalfOpen_Should_Reopen_Circuit()
    {
        var cb = new CircuitBreakerState(1, TimeSpan.FromMilliseconds(50));
        
        cb.RecordFailure();
        await Task.Delay(100);
        
        cb.State.Should().Be(CircuitState.HalfOpen);
        
        cb.RecordFailure();
        cb.State.Should().Be(CircuitState.Open);
    }

    [Fact]
    public async Task Single_Failure_In_HalfOpen_Should_Immediately_Reopen_Circuit_Even_If_Threshold_Is_High()
    {
        var cb = new CircuitBreakerState(5, TimeSpan.FromMilliseconds(50));
        
        for (int i = 0; i < 5; i++) cb.RecordFailure();
        cb.State.Should().Be(CircuitState.Open);
        
        await Task.Delay(100);
        cb.State.Should().Be(CircuitState.HalfOpen);
        
        // A single probe failure in HalfOpen must immediately reopen circuit
        cb.RecordFailure();
        cb.State.Should().Be(CircuitState.Open);
        cb.AllowRequest().Should().BeFalse();
    }
}


