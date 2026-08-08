using System;
using AwesomeAssertions;
using EricksonLopez.Outbox.Contracts;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Contracts;

public class AttributesTests
{
    [Fact]
    public void InboxConsumerAttribute_Should_Set_EventAlias()
    {
        var attr = new InboxConsumerAttribute("test.alias");
        attr.EventAlias.Should().Be("test.alias");
    }

    [Fact]
    public void InboxConsumerAttribute_Should_Throw_On_Empty_Alias()
    {
        Assert.Throws<ArgumentException>(() => new InboxConsumerAttribute(""));
        Assert.Throws<ArgumentException>(() => new InboxConsumerAttribute(" "));
        Assert.Throws<ArgumentException>(() => new InboxConsumerAttribute(null!));
    }

    [Fact]
    public void OutboxMessageAttribute_Should_Set_Alias()
    {
        var attr = new OutboxMessageAttribute("test.alias");
        attr.Alias.Should().Be("test.alias");
    }

    [Fact]
    public void OutboxMessageAttribute_Should_Throw_On_Empty_Alias()
    {
        Assert.Throws<ArgumentException>(() => new OutboxMessageAttribute(""));
        Assert.Throws<ArgumentException>(() => new OutboxMessageAttribute(" "));
        Assert.Throws<ArgumentException>(() => new OutboxMessageAttribute(null!));
    }
}


