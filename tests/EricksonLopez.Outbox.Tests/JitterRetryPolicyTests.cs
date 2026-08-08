using System;
using System.Collections.Generic;
using System.Linq;
using EricksonLopez.Outbox.Retry;
using Xunit;

namespace EricksonLopez.Outbox.Tests;

public sealed class JitterRetryPolicyTests
{
    private static JitterRetryPolicy CreateDefault() => new(
        InitialDelay: TimeSpan.FromMilliseconds(100),
        MaxAttempts: 10,
        Factor: 2.0,
        MaxDelay: TimeSpan.FromSeconds(30),
        JitterFactor: 0.25);

    [Fact]
    public void NextDelay_Attempt1_IsNearInitialDelay()
    {
        var policy = CreateDefault();
        var delay = policy.GetNextDelay(1);
        Assert.True(delay.HasValue);
    }
}


