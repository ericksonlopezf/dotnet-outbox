// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Delivery;

public class DispatchContextTests
{
    [Fact]
    public void Constructor_Should_Initialize_Properties()
    {
        var cts = new CancellationTokenSource();
        var context = new DispatchContext(cts.Token, 5);

        context.CancellationToken.Should().Be(cts.Token);
        context.Attempt.Should().Be(5);
    }
}



