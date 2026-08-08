using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Testing;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Testing;

public class FakeBrokerPublisherTests
{
    [Fact]
    public async Task PublishAsync_Should_Succeed()
    {
        var publisher = new FakeBrokerPublisher();
        var msg = new MessageEnvelope<string>("data", new MessageMetadata("corr", "caus", "type", null));
        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));
        
        result.Success.Should().BeTrue();
        publisher.CapturedMessages.Count.Should().Be(1);
    }

    [Fact]
    public async Task PublishAsync_Should_Fail_When_Configured()
    {
        var publisher = new FakeBrokerPublisher().WithFailure();
        var msg = new MessageEnvelope<string>("data", new MessageMetadata("corr", "caus", "type", null));
        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));
        
        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeTrue();
        publisher.CapturedMessages.Count.Should().Be(0);
    }

    [Fact]
    public async Task PublishBatchAsync_Should_Succeed()
    {
        var publisher = new FakeBrokerPublisher();
        var msg = new MessageEnvelope<string>("data", new MessageMetadata("corr", "caus", "type", null));
        var result = await publisher.PublishBatchAsync(new[] { msg }, new DispatchContext(CancellationToken.None, 1));
        
        result.Count.Should().Be(1);
        result[0].Success.Should().BeTrue();
        publisher.CapturedMessages.Count.Should().Be(1);
    }

    [Fact]
    public async Task PublishRawAsync_Should_Succeed()
    {
        var publisher = new FakeBrokerPublisher();
        var msg = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var meta = new MessageMetadata("corr", "caus", "type", null);
        var result = await publisher.PublishRawAsync(msg, meta, new DispatchContext(CancellationToken.None, 1));
        
        result.Success.Should().BeTrue();
        publisher.CapturedMessages.Count.Should().Be(1);
    }

    [Fact]
    public async Task PublishRawAsync_Should_Fail_When_Configured()
    {
        var publisher = new FakeBrokerPublisher().WithFailure();
        var msg = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var meta = new MessageMetadata("corr", "caus", "type", null);
        var result = await publisher.PublishRawAsync(msg, meta, new DispatchContext(CancellationToken.None, 1));
        
        result.Success.Should().BeFalse();
        publisher.CapturedMessages.Count.Should().Be(0);
    }

    [Fact]
    public async Task Reset_Should_Clear_Messages()
    {
        var publisher = new FakeBrokerPublisher();
        var msg = new MessageEnvelope<string>("data", new MessageMetadata("corr", "caus", "type", null));
        await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));
        
        publisher.Reset();
        publisher.CapturedMessages.Count.Should().Be(0);
    }

    [Fact]
    public async Task ShouldHavePublished_Once_Should_Not_Throw()
    {
        var publisher = new FakeBrokerPublisher();
        var msg = new MessageEnvelope<string>("data", new MessageMetadata("corr", "caus", "type", null));
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
        var msg = new MessageEnvelope<string>("data", new MessageMetadata("corr", "caus", "type", null));
        await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));
        
        publisher.ShouldHavePublished("type").WithCorrelationId("corr").Once();
    }

    [Fact]
    public async Task ShouldHavePublished_WithCorrelationId_Should_Throw()
    {
        var publisher = new FakeBrokerPublisher();
        var msg = new MessageEnvelope<string>("data", new MessageMetadata("corr2", "caus", "type", null));
        await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));
        
        Assert.Throws<InvalidOperationException>(() => publisher.ShouldHavePublished("type").WithCorrelationId("corr").Once());
    }

    [Fact]
    public async Task ShouldHavePublished_AtLeastOnce_Should_Not_Throw()
    {
        var publisher = new FakeBrokerPublisher();
        var msg = new MessageEnvelope<string>("data", new MessageMetadata("corr", "caus", "type", null));
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
        var msg = new MessageEnvelope<string>("data", new MessageMetadata("corr", "caus", "type", null));
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
        var msg = new MessageEnvelope<string>("data", new MessageMetadata("corr", "caus", "type", null));
        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));
        
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ShouldHavePublished_Once_With_Different_MessageType_Should_Throw()
    {
        var publisher = new FakeBrokerPublisher();
        var msg = new MessageEnvelope<string>("data", new MessageMetadata("corr", "caus", "other_type", null));
        await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));
        
        Assert.Throws<InvalidOperationException>(() => publisher.ShouldHavePublished("type").Once());
    }

    [Fact]
    public void PublishedRawMessage_Equals_And_GetHashCode_Work()
    {
        var payload = new byte[] { 1 };
        var msg1 = new PublishedRawMessage("type", payload, new MessageMetadata("corr", "caus", "type", null));
        var msg2 = new PublishedRawMessage("type", payload, new MessageMetadata("corr", "caus", "type", null));
        var msg3 = new PublishedRawMessage("type2", payload, new MessageMetadata("corr", "caus", "type", null));

        msg1.Equals(msg2).Should().BeTrue();
        (msg1 == msg2).Should().BeTrue();
        msg1.GetHashCode().Should().Be(msg2.GetHashCode());

        msg1.Equals(msg3).Should().BeFalse();
        (msg1 != msg3).Should().BeTrue();
    }
}


