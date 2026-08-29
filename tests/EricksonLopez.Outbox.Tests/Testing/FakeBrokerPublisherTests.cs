// Copyright © Erickson Lopez. MIT License.
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Testing;
using EricksonLopez.Result;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Testing;

public class FakeBrokerPublisherTests
{
    [Fact]
    public async Task PublishAsync_Should_Succeed()
    {
        var publisher = new FakeBrokerPublisher();
        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata("corr", "caus", "type", null));
        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));
        
        result.Success.Should().BeTrue();
        publisher.CapturedMessages.Count.Should().Be(1);
    }

    [Fact]
    public async Task PublishAsync_Should_Fail_When_Configured()
    {
        var publisher = new FakeBrokerPublisher().WithFailure();
        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata("corr", "caus", "type", null));
        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));
        
        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeTrue();
        publisher.CapturedMessages.Count.Should().Be(0);
    }

    [Fact]
    public async Task PublishBatchAsync_Should_Succeed()
    {
        var publisher = new FakeBrokerPublisher();
        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata("corr", "caus", "type", null));
        var result = await publisher.PublishBatchAsync(new[] { msg }, new DispatchContext(CancellationToken.None, 1));
        
        result.Count.Should().Be(1);
        result[0].Success.Should().BeTrue();
        publisher.CapturedMessages.Count.Should().Be(1);
    }

    [Fact]
    public async Task PublishRawAsync_Should_Succeed()
    {
        var publisher = new FakeBrokerPublisher();
        var msg = new Infrastructure.OutboxMessageTestDataBuilder().WithMessageType("alias").WithPayload(Array.Empty<byte>()).Build();
        var meta = new OutboxMessageMetadata("corr", "caus", "type", null);
        var result = await publisher.PublishRawAsync(msg, meta, new DispatchContext(CancellationToken.None, 1));
        
        result.Success.Should().BeTrue();
        publisher.CapturedMessages.Count.Should().Be(1);
    }

    [Fact]
    public async Task PublishRawAsync_Should_Fail_When_Configured()
    {
        var publisher = new FakeBrokerPublisher().WithFailure();
        var msg = new Infrastructure.OutboxMessageTestDataBuilder().WithMessageType("alias").WithPayload(Array.Empty<byte>()).Build();
        var meta = new OutboxMessageMetadata("corr", "caus", "type", null);
        var result = await publisher.PublishRawAsync(msg, meta, new DispatchContext(CancellationToken.None, 1));
        
        result.Success.Should().BeFalse();
        publisher.CapturedMessages.Count.Should().Be(0);
    }

    [Fact]
    public async Task Reset_Should_Clear_Messages()
    {
        var publisher = new FakeBrokerPublisher();
        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata("corr", "caus", "type", null));
        await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));
        
        publisher.Reset();
        publisher.CapturedMessages.Count.Should().Be(0);
    }

    [Fact]
    public async Task ShouldHavePublished_Once_Should_Not_Throw()
    {
        var publisher = new FakeBrokerPublisher();
        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata("corr", "caus", "type", null));
        await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));
        
        publisher.ShouldHavePublished("type").Once();
    }

    [Fact]
    public void ShouldHavePublished_Once_Should_Throw()
    {
        var publisher = new FakeBrokerPublisher();
        Assert.Throws<InvalidOperationException>(() => publisher.ShouldHavePublished("type").Once());
    }

    [Fact]
    public async Task ShouldHavePublished_WithCorrelationId_Should_Not_Throw()
    {
        var publisher = new FakeBrokerPublisher();
        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata("corr", "caus", "type", null));
        await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));
        
        publisher.ShouldHavePublished("type").WithCorrelationId("corr").Once();
    }

    [Fact]
    public async Task ShouldHavePublished_WithCorrelationId_Should_Throw()
    {
        var publisher = new FakeBrokerPublisher();
        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata("corr2", "caus", "type", null));
        await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));
        
        Assert.Throws<InvalidOperationException>(() => publisher.ShouldHavePublished("type").WithCorrelationId("corr").Once());
    }

    [Fact]
    public async Task ShouldHavePublished_AtLeastOnce_Should_Not_Throw()
    {
        var publisher = new FakeBrokerPublisher();
        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata("corr", "caus", "type", null));
        await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));
        await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));
        
        publisher.ShouldHavePublished("type").AtLeastOnce();
    }

    [Fact]
    public void ShouldHavePublished_AtLeastOnce_Should_Throw()
    {
        var publisher = new FakeBrokerPublisher();
        Assert.Throws<InvalidOperationException>(() => publisher.ShouldHavePublished("type").AtLeastOnce());
    }

    [Fact]
    public async Task ShouldHavePublished_Never_Should_Throw()
    {
        var publisher = new FakeBrokerPublisher();
        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata("corr", "caus", "type", null));
        await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));
        
        Assert.Throws<InvalidOperationException>(() => publisher.ShouldHavePublished("type").Never());
    }

    [Fact]
    public void ShouldHavePublished_Never_Should_Not_Throw()
    {
        var publisher = new FakeBrokerPublisher();
        publisher.ShouldHavePublished("type").Never();
    }

    [Fact]
    public async Task WithSuccess_Should_Remove_Failure()
    {
        var publisher = new FakeBrokerPublisher().WithFailure().WithSuccess();
        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata("corr", "caus", "type", null));
        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));
        
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ShouldHavePublished_Once_With_Different_MessageType_Should_Throw()
    {
        var publisher = new FakeBrokerPublisher();
        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata("corr", "caus", "other_type", null));
        await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));
        
        Assert.Throws<InvalidOperationException>(() => publisher.ShouldHavePublished("type").Once());
    }

    [Fact]
    public async Task WithFailure_WithCustomException_UsesGivenException()
    {
        var customEx = new InvalidCastException("custom error");
        var publisher = new FakeBrokerPublisher().WithFailure(customEx);
        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata("corr", "caus", "type", null));
        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));
        
        result.Success.Should().BeFalse();
        result.Error.Should().BeSameAs(customEx);
    }

    [Fact]
    public async Task PublishAsync_WhenMessageTypeIsNull_FallsBackToTypeName()
    {
        var publisher = new FakeBrokerPublisher();
        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata("corr", "caus", null, null));
        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeTrue();
        publisher.CapturedMessages.Should().ContainSingle(m => m.MessageType == nameof(String));
    }

    [Fact]
    public async Task ShouldHavePublished_Times_Should_Not_Throw_When_Count_Matches()
    {
        var publisher = new FakeBrokerPublisher();
        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata("corr", "caus", "type", null));
        await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));
        await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));

        publisher.ShouldHavePublished("type").Times(2);
    }

    [Fact]
    public async Task ShouldHavePublished_Times_Should_Throw_When_Count_Does_Not_Match()
    {
        var publisher = new FakeBrokerPublisher();
        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata("corr", "caus", "type", null));
        await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));

        Assert.Throws<InvalidOperationException>(() => publisher.ShouldHavePublished("type").Times(2));
    }

    [Fact]
    public void PublishedRawMessage_Equals_And_GetHashCode_Work()
    {
        var payload = new byte[] { 1 };
        var msg1 = new PublishedRawMessage("type", payload, new OutboxMessageMetadata("corr", "caus", "type", null));
        var msg2 = new PublishedRawMessage("type", payload, new OutboxMessageMetadata("corr", "caus", "type", null));
        var msg3 = new PublishedRawMessage("type2", payload, new OutboxMessageMetadata("corr", "caus", "type", null));

        msg1.Equals(msg2).Should().BeTrue();
        (msg1 == msg2).Should().BeTrue();
        msg1.GetHashCode().Should().Be(msg2.GetHashCode());

        msg1.Equals(msg3).Should().BeFalse();
        (msg1 != msg3).Should().BeTrue();

        var (type, data, meta) = msg1;
        type.Should().Be("type");
        data.ToArray().Should().Equal(payload);
        meta.CorrelationId.Should().Be("corr");
    }
}








