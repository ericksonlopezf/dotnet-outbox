using System;
using AwesomeAssertions;
using Xunit;

namespace EricksonLopez.Outbox.Tests;

public class OutboxMessageTests
{
    [Fact]
    public void Equals_WhenOtherIsNull_ReturnsFalse()
    {
        var msg = CreateMessage();
        msg.Equals(null).Should().BeFalse();
    }

    [Fact]
    public void Equals_WhenOtherIsSameReference_ReturnsTrue()
    {
        var msg = CreateMessage();
        msg.Equals(msg).Should().BeTrue();
    }

    [Fact]
    public void Equals_WhenOtherIsIdentical_ReturnsTrue()
    {
        var msg1 = CreateMessage();
        var msg2 = CreateMessage();
        msg1.Equals(msg2).Should().BeTrue();
        (msg1 == msg2).Should().BeTrue();
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
    public void GetHashCode_WhenIdentical_ReturnsSameHash()
    {
        var msg1 = CreateMessage();
        var msg2 = CreateMessage();
        msg1.GetHashCode().Should().Be(msg2.GetHashCode());
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
}
