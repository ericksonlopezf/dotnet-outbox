// Copyright © Erickson Lopez. MIT License.
using System;
using AwesomeAssertions;
using EricksonLopez.Outbox.Retry;
using FsCheck;
using FsCheck.Xunit;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Resilience;

public sealed class RetryPoliciesTests
{
    [Fact]
    public void CircuitBreakerOpenException_InitializesWithMessage()
    {
        var ex = new CircuitBreakerOpenException("Circuit is open test");
        ex.Message.Should().Be("Circuit is open test");
        ex.Should().BeAssignableTo<Exception>();
    }

    [Fact]
    public void RetryPolicy_Default_HasExpectedValues()
    {
        var policy = RetryPolicy.Default;
        policy.Should().BeOfType<ExponentialBackoffRetryPolicy>();

        var exp = (ExponentialBackoffRetryPolicy)policy;
        exp.InitialDelay.Should().Be(TimeSpan.FromSeconds(1));
        exp.MaxAttempts.Should().Be(5);
        exp.Factor.Should().Be(2.0);
        exp.MaxDelay.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void FixedDelayRetryPolicy_GetNextDelay_ReturnsDelayUntilMaxAttempts()
    {
        var policy = new FixedDelayRetryPolicy(TimeSpan.FromMilliseconds(500), 3);

        policy.Delay.Should().Be(TimeSpan.FromMilliseconds(500));
        policy.MaxAttempts.Should().Be(3);

        policy.GetNextDelay(0).Should().Be(TimeSpan.FromMilliseconds(500));
        policy.GetNextDelay(1).Should().Be(TimeSpan.FromMilliseconds(500));
        policy.GetNextDelay(2).Should().Be(TimeSpan.FromMilliseconds(500));
        policy.GetNextDelay(3).Should().BeNull();
        policy.GetNextDelay(4).Should().BeNull();

        // Record equality and mutation
        var copy = policy with { Delay = TimeSpan.FromSeconds(1) };
        copy.Delay.Should().Be(TimeSpan.FromSeconds(1));
        (policy == copy).Should().BeFalse();
        (policy == (policy with { })).Should().BeTrue();
    }

    [Fact]
    public void ExponentialBackoffRetryPolicy_GetNextDelay_CalculatesExponentialDelays()
    {
        var policy = new ExponentialBackoffRetryPolicy(
            InitialDelay: TimeSpan.FromSeconds(1),
            MaxAttempts: 4,
            Factor: 2.0,
            MaxDelay: null);

        policy.InitialDelay.Should().Be(TimeSpan.FromSeconds(1));
        policy.MaxAttempts.Should().Be(4);
        policy.Factor.Should().Be(2.0);
        policy.MaxDelay.Should().BeNull();

        // Attempt 1: 1s * 2^0 = 1s
        policy.GetNextDelay(1).Should().Be(TimeSpan.FromSeconds(1));
        // Attempt 2: 1s * 2^1 = 2s
        policy.GetNextDelay(2).Should().Be(TimeSpan.FromSeconds(2));
        // Attempt 3: 1s * 2^2 = 4s
        policy.GetNextDelay(3).Should().Be(TimeSpan.FromSeconds(4));
        // Attempt 4: MaxAttempts reached -> null
        policy.GetNextDelay(4).Should().BeNull();
        policy.GetNextDelay(5).Should().BeNull();
    }

    [Fact]
    public void ExponentialBackoffRetryPolicy_CapsAtMaxDelay_WhenExceeded()
    {
        var policy = new ExponentialBackoffRetryPolicy(
            InitialDelay: TimeSpan.FromSeconds(1),
            MaxAttempts: 5,
            Factor: 3.0,
            MaxDelay: TimeSpan.FromSeconds(5));

        policy.GetNextDelay(1).Should().Be(TimeSpan.FromSeconds(1)); // 1s
        policy.GetNextDelay(2).Should().Be(TimeSpan.FromSeconds(3)); // 3s
        policy.GetNextDelay(3).Should().Be(TimeSpan.FromSeconds(5)); // 9s capped to 5s
        policy.GetNextDelay(4).Should().Be(TimeSpan.FromSeconds(5)); // 27s capped to 5s
        policy.GetNextDelay(5).Should().BeNull();

        // Record equality
        var same = policy with { };
        (policy == same).Should().BeTrue();
    }

    [Fact]
    public void ExponentialBackoffRetryPolicy_WhenDelayEqualsMaxDelay_ReturnsMaxDelay()
    {
        var policy = new ExponentialBackoffRetryPolicy(
            InitialDelay: TimeSpan.FromSeconds(2),
            MaxAttempts: 3,
            Factor: 2.0,
            MaxDelay: TimeSpan.FromSeconds(4));

        policy.GetNextDelay(1).Should().Be(TimeSpan.FromSeconds(2));
        policy.GetNextDelay(2).Should().Be(TimeSpan.FromSeconds(4)); // Exactly MaxDelay
        policy.GetNextDelay(3).Should().BeNull();
    }

    [Fact]
    public void ExponentialBackoffPolicy_IRetryPolicy_DefaultsAndBehaviors()
    {
        var defaultPolicy = new ExponentialBackoffPolicy();
        defaultPolicy.GetNextDelay(1).Should().Be(TimeSpan.FromMilliseconds(100));
        defaultPolicy.GetNextDelay(2).Should().Be(TimeSpan.FromMilliseconds(200));
        defaultPolicy.GetNextDelay(3).Should().Be(TimeSpan.FromMilliseconds(400));
        defaultPolicy.GetNextDelay(4).Should().Be(TimeSpan.FromMilliseconds(800));

        defaultPolicy.ShouldRetry(1, new InvalidOperationException()).Should().BeTrue();
        defaultPolicy.ShouldRetry(9, new InvalidOperationException()).Should().BeTrue();
        defaultPolicy.ShouldRetry(10, new InvalidOperationException()).Should().BeFalse();
        defaultPolicy.ShouldRetry(11, new InvalidOperationException()).Should().BeFalse();

        var customPolicy = new ExponentialBackoffPolicy(maxAttempts: 3, initialDelayMs: 50);
        customPolicy.GetNextDelay(1).Should().Be(TimeSpan.FromMilliseconds(50));
        customPolicy.GetNextDelay(2).Should().Be(TimeSpan.FromMilliseconds(100));
        customPolicy.ShouldRetry(1, null!).Should().BeTrue();
        customPolicy.ShouldRetry(2, null!).Should().BeTrue();
        customPolicy.ShouldRetry(3, null!).Should().BeFalse();
    }

    [Fact]
    public void JitterRetryPolicy_PropertiesAndDefaults()
    {
        var policy = new JitterRetryPolicy(TimeSpan.FromMilliseconds(200), 5);
        policy.InitialDelay.Should().Be(TimeSpan.FromMilliseconds(200));
        policy.MaxAttempts.Should().Be(5);
        policy.Factor.Should().Be(2.0);
        policy.MaxDelay.Should().BeNull();
        policy.JitterFactor.Should().Be(0.25);

        var next = policy.GetNextDelay(1);
        next.Should().NotBeNull();
        next!.Value.TotalMilliseconds.Should().BeInRange(150, 250);

        policy.GetNextDelay(5).Should().BeNull();
        policy.GetNextDelay(6).Should().BeNull();
    }

    [Fact]
    public void JitterRetryPolicy_InternalConstructor_CustomRandomProvider()
    {
        // Provider returning 0.0 -> (2 * 0 - 1) = -1.0 -> jitter = -baseMs * JitterFactor
        var minJitterPolicy = new JitterRetryPolicy(
            initialDelay: TimeSpan.FromSeconds(1),
            maxAttempts: 4,
            factor: 2.0,
            maxDelay: null,
            jitterFactor: 0.2,
            randomDoubleProvider: () => 0.0);

        // Attempt 1: base = 1000ms, jitter = 1000 * 0.2 * (-1) = -200ms -> 800ms
        minJitterPolicy.GetNextDelay(1)!.Value.TotalMilliseconds.Should().Be(800);

        // Attempt 2: base = 2000ms, jitter = 2000 * 0.2 * (-1) = -400ms -> 1600ms
        minJitterPolicy.GetNextDelay(2)!.Value.TotalMilliseconds.Should().Be(1600);

        // Provider returning 0.5 -> (2 * 0.5 - 1) = 0.0 -> jitter = 0
        var zeroJitterPolicy = new JitterRetryPolicy(
            initialDelay: TimeSpan.FromSeconds(1),
            maxAttempts: 4,
            factor: 2.0,
            maxDelay: null,
            jitterFactor: 0.2,
            randomDoubleProvider: () => 0.5);

        zeroJitterPolicy.GetNextDelay(1)!.Value.TotalMilliseconds.Should().Be(1000);
        zeroJitterPolicy.GetNextDelay(2)!.Value.TotalMilliseconds.Should().Be(2000);

        // Provider returning 1.0 -> (2 * 1 - 1) = 1.0 -> jitter = +baseMs * JitterFactor
        var maxJitterPolicy = new JitterRetryPolicy(
            initialDelay: TimeSpan.FromSeconds(1),
            maxAttempts: 4,
            factor: 2.0,
            maxDelay: null,
            jitterFactor: 0.2,
            randomDoubleProvider: () => 1.0);

        maxJitterPolicy.GetNextDelay(1)!.Value.TotalMilliseconds.Should().Be(1200);
        maxJitterPolicy.GetNextDelay(2)!.Value.TotalMilliseconds.Should().Be(2400);

        // Null provider fallback
        var nullProviderPolicy = new JitterRetryPolicy(
            initialDelay: TimeSpan.FromSeconds(1),
            maxAttempts: 3,
            factor: 2.0,
            maxDelay: null,
            jitterFactor: 0.2,
            randomDoubleProvider: null!);

        nullProviderPolicy.GetNextDelay(1).Should().NotBeNull();
    }

    [Fact]
    public void JitterRetryPolicy_MaxDelayCapping_AppliesBeforeJitter()
    {
        var policy = new JitterRetryPolicy(
            initialDelay: TimeSpan.FromSeconds(1),
            maxAttempts: 5,
            factor: 2.0,
            maxDelay: TimeSpan.FromSeconds(2),
            jitterFactor: 0.1,
            randomDoubleProvider: () => 1.0);

        // Attempt 1: base = 1000ms (< 2000ms). Jitter = 1000 * 0.1 * 1 = 100ms -> 1100ms
        policy.GetNextDelay(1)!.Value.TotalMilliseconds.Should().Be(1100);

        // Attempt 2: base = 2000ms (== 2000ms). Jitter = 2000 * 0.1 * 1 = 200ms -> 2200ms
        policy.GetNextDelay(2)!.Value.TotalMilliseconds.Should().Be(2200);

        // Attempt 3: base would be 4000ms -> capped to 2000ms. Jitter = 2000 * 0.1 * 1 = 200ms -> 2200ms
        policy.GetNextDelay(3)!.Value.TotalMilliseconds.Should().Be(2200);
    }

    [Fact]
    public void JitterRetryPolicy_NegativeCalculatedDelay_ClampedToZero()
    {
        // If JitterFactor is > 1.0 (e.g. 1.5) and random returns 0.0:
        // baseMs + baseMs * 1.5 * (-1) = -0.5 * baseMs < 0 -> clamped to 0
        var extremeJitterPolicy = new JitterRetryPolicy(
            initialDelay: TimeSpan.FromSeconds(1),
            maxAttempts: 3,
            factor: 2.0,
            maxDelay: null,
            jitterFactor: 1.5,
            randomDoubleProvider: () => 0.0);

        extremeJitterPolicy.GetNextDelay(1)!.Value.Should().Be(TimeSpan.Zero);
    }
}
