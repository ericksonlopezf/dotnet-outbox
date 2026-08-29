// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using AwesomeAssertions;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Delivery;

public class OutboxMessageTests
{
    [Fact]
    public void Equals_WhenOtherIsNull_ReturnsFalse()
    {
        var msg = CreateMessage();
        msg.Equals(null).Should().BeFalse();
        (msg == null).Should().BeFalse();
        (null == msg).Should().BeFalse();
        (msg != null).Should().BeTrue();
    }

    [Fact]
    public void Equals_WhenOtherIsSameReference_ReturnsTrue()
    {
        var msg = CreateMessage();
        var msgRef = msg;
        msg.Equals(msg).Should().BeTrue();
        msg.Equals((object)msg).Should().BeTrue();
        (msg == msgRef).Should().BeTrue();
        (msg != msgRef).Should().BeFalse();
    }

    [Fact]
    public void Equals_WhenOtherIsIdentical_ReturnsTrue()
    {
        var msg1 = CreateMessage();
        var msg2 = CreateMessage();
        msg1.Equals(msg2).Should().BeTrue();
        msg1.Equals((object)msg2).Should().BeTrue();
        (msg1 == msg2).Should().BeTrue();
        (msg1 != msg2).Should().BeFalse();
    }

    [Fact]
    public void Equals_WhenOtherIsIdentical_WithAllNonNullFields_ReturnsTrue()
    {
        var msg1 = CreateFullyPopulatedMessage();
        var msg2 = CreateFullyPopulatedMessage();
        msg1.Equals(msg2).Should().BeTrue();
        msg1.Equals((object)msg2).Should().BeTrue();
        (msg1 == msg2).Should().BeTrue();
        (msg1 != msg2).Should().BeFalse();
    }

    [Fact]
    public void Equals_WhenOtherHasDifferentProperty_ReturnsFalse()
    {
        var baseMsg = CreateMessage();

        baseMsg.Equals(baseMsg with { Id = Guid.NewGuid() }).Should().BeFalse();
        baseMsg.Equals(baseMsg with { MessageType = "Other" }).Should().BeFalse();
        baseMsg.Equals(baseMsg with { CorrelationId = "Other" }).Should().BeFalse();
        baseMsg.Equals(baseMsg with { CausationId = "Other" }).Should().BeFalse();
        baseMsg.Equals(baseMsg with { CreatedAt = DateTimeOffset.UtcNow }).Should().BeFalse();
        baseMsg.Equals(baseMsg with { ProcessedAt = DateTimeOffset.UtcNow }).Should().BeFalse();
        baseMsg.Equals(baseMsg with { DeliverAt = DateTimeOffset.UtcNow }).Should().BeFalse();
        baseMsg.Equals(baseMsg with { Status = OutboxMessageStatus.Dispatched }).Should().BeFalse();
        baseMsg.Equals(baseMsg with { RetryCount = 99 }).Should().BeFalse();
        baseMsg.Equals(baseMsg with { Error = "Error" }).Should().BeFalse();
        baseMsg.Equals(baseMsg with { Payload = new byte[] { 9, 9, 9 } }).Should().BeFalse();
        baseMsg.Equals(baseMsg with { Headers = new byte[] { 9, 9, 9 } }).Should().BeFalse();
    }

    [Fact]
    public void Equals_WhenFullyPopulatedHasNullProperty_ReturnsFalse()
    {
        var baseMsg = CreateFullyPopulatedMessage();

        baseMsg.Equals(baseMsg with { CorrelationId = null }).Should().BeFalse();
        baseMsg.Equals(baseMsg with { CausationId = null }).Should().BeFalse();
        baseMsg.Equals(baseMsg with { ProcessedAt = null }).Should().BeFalse();
        baseMsg.Equals(baseMsg with { DeliverAt = null }).Should().BeFalse();
        baseMsg.Equals(baseMsg with { Error = null }).Should().BeFalse();
    }

    [Fact]
    public void GetHashCode_WhenIdentical_ReturnsSameHash()
    {
        var msg1 = CreateMessage();
        var msg2 = CreateMessage();
        msg1.GetHashCode().Should().Be(msg2.GetHashCode());

        var full1 = CreateFullyPopulatedMessage();
        var full2 = CreateFullyPopulatedMessage();
        full1.GetHashCode().Should().Be(full2.GetHashCode());
    }

    [Fact]
    public void GetHashCode_WhenPropertiesDiffer_ReturnsDifferentHash()
    {
        var baseMsg = CreateFullyPopulatedMessage();
        var baseHash = baseMsg.GetHashCode();

        (baseMsg with { Id = Guid.NewGuid() }).GetHashCode().Should().NotBe(baseHash);
        (baseMsg with { MessageType = "DifferentType" }).GetHashCode().Should().NotBe(baseHash);
        (baseMsg with { CorrelationId = "DiffCorr" }).GetHashCode().Should().NotBe(baseHash);
        (baseMsg with { CausationId = "DiffCaus" }).GetHashCode().Should().NotBe(baseHash);
        (baseMsg with { CreatedAt = DateTimeOffset.UtcNow.AddDays(1) }).GetHashCode().Should().NotBe(baseHash);
        (baseMsg with { ProcessedAt = DateTimeOffset.UtcNow.AddDays(2) }).GetHashCode().Should().NotBe(baseHash);
        (baseMsg with { DeliverAt = DateTimeOffset.UtcNow.AddDays(3) }).GetHashCode().Should().NotBe(baseHash);
        (baseMsg with { Status = OutboxMessageStatus.Dispatched }).GetHashCode().Should().NotBe(baseHash);
        (baseMsg with { RetryCount = 999 }).GetHashCode().Should().NotBe(baseHash);
        (baseMsg with { Error = "DiffError" }).GetHashCode().Should().NotBe(baseHash);
        (baseMsg with { Payload = new byte[] { 1, 2, 3, 4, 5, 6, 7 } }).GetHashCode().Should().NotBe(baseHash);
        (baseMsg with { Headers = new byte[] { 8, 9, 10, 11, 12, 13, 14 } }).GetHashCode().Should().NotBe(baseHash);
    }

    [Fact]
    public void Extensions_CanBeSetAndRetrieved()
    {
        var extensions = new Dictionary<string, string> { ["custom-key"] = "custom-value" };
        var msg = CreateMessage() with { Extensions = extensions };
        msg.Extensions.Should().NotBeNull();
        msg.Extensions!["custom-key"].Should().Be("custom-value");
    }

    private static OutboxMessage CreateMessage()
    {
        return new OutboxMessage(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            "TestType",
            new byte[] { 1, 2, 3 },
            "CorrId",
            "CausId",
            new byte[] { 4, 5, 6 },
            new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
            null,
            null,
            OutboxMessageStatus.Pending,
            0,
            null
        );
    }

    private static OutboxMessage CreateFullyPopulatedMessage()
    {
        return new OutboxMessage(
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "TestTypeFull",
            new byte[] { 10, 20, 30 },
            "CorrIdFull",
            "CausIdFull",
            new byte[] { 40, 50, 60 },
            new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2020, 1, 1, 1, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2020, 1, 1, 2, 0, 0, TimeSpan.Zero),
            OutboxMessageStatus.InFlight,
            3,
            "SomeError"
        );
    }
}
