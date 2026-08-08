using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RabbitMQ.Client;
using AwesomeAssertions;
using EricksonLopez.Outbox.RabbitMQ;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Serialization;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;
#pragma warning disable CA2012 // NSubstitute generates ValueTasks that aren't awaited in Returns()

namespace EricksonLopez.Outbox.Tests.Brokers;

public class RabbitMQBrokerPublisherTests
{
    [Fact]
    public async Task PublishAsync_Should_Succeed()
    {
        var channel = Substitute.For<IChannel>();
        var serializer = Substitute.For<IOutboxSerializer>();
        serializer.Serialize("data").Returns(new byte[] { 1, 2, 3 });

        var publisher = new RabbitMQBrokerPublisher(channel, serializer, "exchange");

        var msg = new MessageEnvelope<string>("data", new MessageMetadata("corr", "caus", "type", new[] { new MetadataEntry("k", "v") }));
        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeTrue();
        await channel.Received(1).BasicPublishAsync("exchange", "type", true, Arg.Is<BasicProperties>(p => 
            p.CorrelationId == "corr" &&
            p.Headers != null &&
            p.Headers["k"] as string == "v"
        ), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_Should_Fail()
    {
        var channel = Substitute.For<IChannel>();
        _ = channel.BasicPublishAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<BasicProperties>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromException(new InvalidOperationException("test")));
        
        var serializer = Substitute.For<IOutboxSerializer>();
        var publisher = new RabbitMQBrokerPublisher(channel, serializer, "exchange");

        var msg = new MessageEnvelope<string>("data", new MessageMetadata(null, null, null));
        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeTrue();
    }

    [Fact]
    public async Task PublishBatchAsync_Should_Succeed()
    {
        var channel = Substitute.For<IChannel>();
        var serializer = Substitute.For<IOutboxSerializer>();
        var publisher = new RabbitMQBrokerPublisher(channel, serializer, "exchange");

        var msg = new MessageEnvelope<string>("data", new MessageMetadata(null, null, null));
        var result = await publisher.PublishBatchAsync(new[] { msg }, new DispatchContext(CancellationToken.None, 1));

        result.Count.Should().Be(1);
        result[0].Success.Should().BeTrue();
    }

    [Fact]
    public async Task PublishRawAsync_Should_Succeed()
    {
        var channel = Substitute.For<IChannel>();
        var serializer = Substitute.For<IOutboxSerializer>();

        var publisher = new RabbitMQBrokerPublisher(channel, serializer, "exchange");

        var msg = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var meta = new MessageMetadata("corr", "caus", "type", new[] { new MetadataEntry("k", "v") });
        var result = await publisher.PublishRawAsync(msg, meta, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeTrue();
        await channel.Received(1).BasicPublishAsync("exchange", "alias", true, Arg.Is<BasicProperties>(p => 
            p.CorrelationId == "corr" &&
            p.Headers != null &&
            p.Headers["k"] as string == "v"
        ), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishRawAsync_Should_Fail()
    {
        var channel = Substitute.For<IChannel>();
        _ = channel.BasicPublishAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<BasicProperties>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromException(new InvalidOperationException("test")));
        
        var serializer = Substitute.For<IOutboxSerializer>();

        var publisher = new RabbitMQBrokerPublisher(channel, serializer, "exchange");

        var msg = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var meta = new MessageMetadata(null, null, null);
        var result = await publisher.PublishRawAsync(msg, meta, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeTrue();
    }
}



