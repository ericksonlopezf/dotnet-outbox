// Copyright © Erickson Lopez. MIT License.
using System;
using AwesomeAssertions;
using EricksonLopez.Inbox.Configuration;
using Xunit;

namespace EricksonLopez.Inbox.Tests;

public sealed class InboxOptionsTests
{
    [Fact]
    public void DefaultOptions_HaveExpectedValues()
    {
        var options = new InboxOptions();

        options.RetentionPeriod.Should().Be(TimeSpan.FromDays(7));
        options.CleanupInterval.Should().Be(TimeSpan.FromHours(1));
        options.EnableAutomaticCleanup.Should().BeTrue();
    }

    [Fact]
    public void PropertySetters_UpdateValuesCorrectly()
    {
        var options = new InboxOptions
        {
            RetentionPeriod = TimeSpan.FromDays(30),
            CleanupInterval = TimeSpan.FromMinutes(15),
            EnableAutomaticCleanup = false
        };

        options.RetentionPeriod.Should().Be(TimeSpan.FromDays(30));
        options.CleanupInterval.Should().Be(TimeSpan.FromMinutes(15));
        options.EnableAutomaticCleanup.Should().BeFalse();
    }
}
