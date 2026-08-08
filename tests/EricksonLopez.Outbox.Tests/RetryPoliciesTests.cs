using System;
using AwesomeAssertions;
using EricksonLopez.Outbox.Retry;
using Xunit;

namespace EricksonLopez.Outbox.Tests;

public sealed class RetryPoliciesTests
{
    [Fact]
    public void FixedDelayRetryPolicy_Should_Return_Delay_Until_MaxAttempts()
    {
        var policy = new FixedDelayRetryPolicy(TimeSpan.FromSeconds(1), 3);
        
        policy.GetNextDelay(1).Should().Be(TimeSpan.FromSeconds(1));
        policy.GetNextDelay(2).Should().Be(TimeSpan.FromSeconds(1));
        policy.GetNextDelay(3).Should().BeNull();
        policy.GetNextDelay(4).Should().BeNull();
    }

    [Fact]
    public void ExponentialBackoffRetryPolicy_Should_Return_Exponential_Delays()
    {
        var policy = new ExponentialBackoffRetryPolicy(TimeSpan.FromSeconds(1), 4, 2.0);
        
        policy.GetNextDelay(1).Should().Be(TimeSpan.FromSeconds(1));
        policy.GetNextDelay(2).Should().Be(TimeSpan.FromSeconds(2));
        policy.GetNextDelay(3).Should().Be(TimeSpan.FromSeconds(4));
        policy.GetNextDelay(4).Should().BeNull();
    }

    [Fact]
    public void ExponentialBackoffRetryPolicy_Should_Cap_At_MaxDelay()
    {
        var policy = new ExponentialBackoffRetryPolicy(TimeSpan.FromSeconds(1), 5, 2.0, TimeSpan.FromSeconds(3));
        
        policy.GetNextDelay(1).Should().Be(TimeSpan.FromSeconds(1));
        policy.GetNextDelay(2).Should().Be(TimeSpan.FromSeconds(2));
        policy.GetNextDelay(3).Should().Be(TimeSpan.FromSeconds(3)); // Capped
        policy.GetNextDelay(4).Should().Be(TimeSpan.FromSeconds(3)); // Capped
        policy.GetNextDelay(5).Should().BeNull();
    }

    [Fact]
    public void JitterRetryPolicy_Should_Add_Random_Jitter()
    {
        var policy = new JitterRetryPolicy(TimeSpan.FromSeconds(1), 4, 2.0, null, 0.25);
        
        // Attempt 1: base = 1s, jitter = +/- 250ms -> 750ms to 1250ms
        var delay1 = policy.GetNextDelay(1);
        delay1.Should().NotBeNull();
        delay1.Value.TotalMilliseconds.Should().BeInRange(750, 1250);

        // Attempt 2: base = 2s, jitter = +/- 500ms -> 1500ms to 2500ms
        var delay2 = policy.GetNextDelay(2);
        delay2.Should().NotBeNull();
        delay2.Value.TotalMilliseconds.Should().BeInRange(1500, 2500);

        policy.GetNextDelay(4).Should().BeNull();
    }

    [Fact]
    public void JitterRetryPolicy_Should_Cap_At_MaxDelay()
    {
        var policy = new JitterRetryPolicy(TimeSpan.FromSeconds(1), 5, 2.0, TimeSpan.FromSeconds(1.5), 0.25);
        
        // Attempt 3: base would be 4s, capped to 1.5s. Jitter +/- 25% of 1.5s = +/- 375ms -> 1125 to 1875
        var delay3 = policy.GetNextDelay(3);
        delay3.Should().NotBeNull();
        delay3.Value.TotalMilliseconds.Should().BeInRange(1125, 1875);
    }
}


