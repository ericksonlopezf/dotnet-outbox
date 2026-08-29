// Copyright © Erickson Lopez. MIT License.
using System;
using Xunit;

namespace EricksonLopez.Inbox.Tests;

public sealed class InboxEntryTests
{
    [Fact]
    public void IsEmpty_GivenEmptyStrings_ReturnsTrue()
    {
        var entry = new InboxEntry(string.Empty, string.Empty, DateTimeOffset.UtcNow);
        Assert.True(entry.IsEmpty);
    }

    [Fact]
    public void IsEmpty_GivenMessageIdEmptyButConsumerValid_ReturnsFalse()
    {
        var entry = new InboxEntry(string.Empty, "consumer-1", DateTimeOffset.UtcNow);
        Assert.False(entry.IsEmpty);
    }

    [Fact]
    public void IsEmpty_GivenMessageIdValidButConsumerEmpty_ReturnsFalse()
    {
        var entry = new InboxEntry("msg-1", string.Empty, DateTimeOffset.UtcNow);
        Assert.False(entry.IsEmpty);
    }

    [Fact]
    public void IsEmpty_GivenValidStrings_ReturnsFalse()
    {
        var entry = new InboxEntry("msg-1", "consumer-1", DateTimeOffset.UtcNow);
        Assert.False(entry.IsEmpty);
    }
}
