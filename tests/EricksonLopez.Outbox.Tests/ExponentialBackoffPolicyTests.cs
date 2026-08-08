using System;
using AwesomeAssertions;
using EricksonLopez.Outbox.Retry;
using Xunit;

namespace EricksonLopez.Outbox.Tests;

public class ExponentialBackoffPolicyTests
{
    [Fact]
    public void GetNextDelay_Should_Return_Exponential_Delay()
    {
        var policy = new ExponentialBackoffPolicy(maxAttempts: 10, initialDelayMs: 100);

        policy.GetNextDelay(1).TotalMilliseconds.Should().Be(100);
        policy.GetNextDelay(2).TotalMilliseconds.Should().Be(200);
        policy.GetNextDelay(3).TotalMilliseconds.Should().Be(400);
    }

    [Fact]
    public void ShouldRetry_Should_Return_True_When_Under_Limit()
    {
        var policy = new ExponentialBackoffPolicy(maxAttempts: 3, initialDelayMs: 100);

        policy.ShouldRetry(1, new InvalidOperationException()).Should().BeTrue();
        policy.ShouldRetry(2, new InvalidOperationException()).Should().BeTrue();
    }

    [Fact]
    public void ShouldRetry_Should_Return_False_When_Limit_Reached()
    {
        var policy = new ExponentialBackoffPolicy(maxAttempts: 3, initialDelayMs: 100);

        policy.ShouldRetry(3, new InvalidOperationException()).Should().BeFalse();
        policy.ShouldRetry(4, new InvalidOperationException()).Should().BeFalse();
    }
}


