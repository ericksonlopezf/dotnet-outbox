// Copyright © Erickson Lopez. MIT License.
using System;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Contracts;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Contracts;

public class AttributesTests
{
    [Fact]
    public void IdempotentConsumerAttribute_CanBeInstantiated()
    {
        var attr = new IdempotentConsumerAttribute();
        attr.Should().NotBeNull();
    }

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

    [Fact]
    public void OutboxConstants_DispatcherConsumerId_ShouldBeExpectedValue()
    {
        OutboxConstants.DispatcherConsumerId.Should().Be("outbox-dispatcher");
    }

    [Fact]
    public void OutboxMessageStatus_EnumValues_ShouldMatchContract()
    {
        ((int)OutboxMessageStatus.Pending).Should().Be(0);
        ((int)OutboxMessageStatus.InFlight).Should().Be(1);
        ((int)OutboxMessageStatus.Dispatched).Should().Be(2);
        ((int)OutboxMessageStatus.Failed).Should().Be(3);
        ((int)OutboxMessageStatus.DeadLettered).Should().Be(4);
    }
}
