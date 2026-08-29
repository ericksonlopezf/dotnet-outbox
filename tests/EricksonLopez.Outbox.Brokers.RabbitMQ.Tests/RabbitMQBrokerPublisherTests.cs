// Copyright © Erickson Lopez. MIT License.
#pragma warning disable CA2012 // NSubstitute generates ValueTasks that aren't awaited in Returns()
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.RabbitMQ;
using EricksonLopez.Outbox.Serialization;
using EricksonLopez.Result;
using NSubstitute;
using RabbitMQ.Client;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Brokers;

public class RabbitMQBrokerPublisherTests
{
    [Fact]
    public void Constructor_NullGuards()
    {
        var channel = Substitute.For<IChannel>();
        var serializer = Substitute.For<IOutboxSerializer>();

        Action act1 = () => { _ = new RabbitMQBrokerPublisher(null!, serializer); };
        act1.Should().Throw<ArgumentNullException>().WithParameterName("channel");

        Action act2 = () => { _ = new RabbitMQBrokerPublisher(channel, null!); };
        act2.Should().Throw<ArgumentNullException>().WithParameterName("serializer");
    }

    [Fact]
    public async Task Constructor_DefaultExchangeName_UsesOutboxExchange()
    {
        var channel = Substitute.For<IChannel>();
        var serializer = Substitute.For<IOutboxSerializer>();
        serializer.Serialize("data").Returns(new byte[] { 1 });

        var publisher = new RabbitMQBrokerPublisher(channel, serializer);
        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata("corr", "caus", "type"));

        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));
        result.Success.Should().BeTrue();

        await channel.Received(1).BasicPublishAsync(
            "outbox.exchange",
            "type",
            true,
            Arg.Any<BasicProperties>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_WhenMessageTypeIsNull_PublishesWithEmptyRoutingKey()
    {
        var channel = Substitute.For<IChannel>();
        var serializer = Substitute.For<IOutboxSerializer>();
        serializer.Serialize("data").Returns(new byte[] { 1 });

        var publisher = new RabbitMQBrokerPublisher(channel, serializer, "exchange");
        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata(null, null, null));

        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));
        result.Success.Should().BeTrue();

        await channel.Received(1).BasicPublishAsync(
            "exchange",
            "",
            true,
            Arg.Any<BasicProperties>(),
            Arg.Any<ReadOnlyMemory<byte>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_WhenMessageIsValid_PublishesToChannel()
    {
        var channel = Substitute.For<IChannel>();
        var serializer = Substitute.For<IOutboxSerializer>();
        serializer.Serialize("data").Returns(new byte[] { 1, 2, 3 });

        var publisher = new RabbitMQBrokerPublisher(channel, serializer, "exchange");

        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata("corr", "caus", "type", new[] { new MetadataEntry("k", "v") }));
        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeTrue();
        await channel.Received(1).BasicPublishAsync("exchange", "type", true, Arg.Is<BasicProperties>(p => 
            p.CorrelationId == "corr" &&
            p.Headers != null &&
            p.Headers["k"] as string == "v"
        ), Arg.Is<ReadOnlyMemory<byte>>(b => b.Length == 3), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_WhenCancellationTokenProvided_PropagatesToChannel()
    {
        var channel = Substitute.For<IChannel>();
        var serializer = Substitute.For<IOutboxSerializer>();
        serializer.Serialize("data").Returns(new byte[] { 1, 2, 3 });

        var publisher = new RabbitMQBrokerPublisher(channel, serializer, "exchange");

        using var cts = new CancellationTokenSource();
        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata("corr", "caus", "type", null));
        var result = await publisher.PublishAsync(msg, new DispatchContext(cts.Token, 1));

        result.Success.Should().BeTrue();
        await channel.Received(1).BasicPublishAsync(
            "exchange", 
            "type", 
            true, 
            Arg.Is<BasicProperties>(p => p.CorrelationId == "corr"), 
            Arg.Is<ReadOnlyMemory<byte>>(b => b.Length == 3), 
            cts.Token);
    }

    [Fact]
    public async Task PublishAsync_WhenChannelThrows_ReturnsFailAndRetry()
    {
        var channel = Substitute.For<IChannel>();
        var expectedEx = new InvalidOperationException("Broker disconnected");
        _ = channel.BasicPublishAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<BasicProperties>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromException(expectedEx));
        
        var serializer = Substitute.For<IOutboxSerializer>();
        var publisher = new RabbitMQBrokerPublisher(channel, serializer, "exchange");

        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata(null, null, null));
        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeTrue();
        result.Error.Should().BeSameAs(expectedEx);
    }

    [Fact]
    public async Task PublishBatchAsync_WhenMultipleMessages_PublishesAllToChannel()
    {
        var channel = Substitute.For<IChannel>();
        var serializer = Substitute.For<IOutboxSerializer>();
        serializer.Serialize(Arg.Any<string>()).Returns(new byte[] { 1 });
        var publisher = new RabbitMQBrokerPublisher(channel, serializer, "exchange");

        var msg1 = new MessageEnvelope<string>("data1", new OutboxMessageMetadata("c1", null, "t1"));
        var msg2 = new MessageEnvelope<string>("data2", new OutboxMessageMetadata("c2", null, "t2"));
        var result = await publisher.PublishBatchAsync(new[] { msg1, msg2 }, new DispatchContext(CancellationToken.None, 1));

        result.Count.Should().Be(2);
        result[0].Success.Should().BeTrue();
        result[1].Success.Should().BeTrue();

        await channel.Received(1).BasicPublishAsync("exchange", "t1", true, Arg.Is<BasicProperties>(p => p.CorrelationId == "c1"), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>());
        await channel.Received(1).BasicPublishAsync("exchange", "t2", true, Arg.Is<BasicProperties>(p => p.CorrelationId == "c2"), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishRawAsync_WhenMessageIsValid_PublishesRawPayloadToChannel()
    {
        var channel = Substitute.For<IChannel>();
        var serializer = Substitute.For<IOutboxSerializer>();

        var publisher = new RabbitMQBrokerPublisher(channel, serializer, "exchange");

        var rawPayload = new byte[] { 10, 20, 30 };
        var msg = new OutboxMessage(Guid.NewGuid(), "alias", rawPayload, null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var meta = new OutboxMessageMetadata("corr", "caus", "type", new[] { new MetadataEntry("k", "v") });
        var result = await publisher.PublishRawAsync(msg, meta, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeTrue();
        await channel.Received(1).BasicPublishAsync("exchange", "alias", true, Arg.Is<BasicProperties>(p => 
            p.CorrelationId == "corr" &&
            p.Headers != null &&
            p.Headers["k"] as string == "v"
        ), Arg.Is<ReadOnlyMemory<byte>>(b => b.Length == 3), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishRawAsync_WhenChannelThrows_ReturnsFailAndRetry()
    {
        var channel = Substitute.For<IChannel>();
        var expectedEx = new InvalidOperationException("Broker disconnected");
        _ = channel.BasicPublishAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<BasicProperties>(), Arg.Any<ReadOnlyMemory<byte>>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromException(expectedEx));
        
        var serializer = Substitute.For<IOutboxSerializer>();

        var publisher = new RabbitMQBrokerPublisher(channel, serializer, "exchange");

        var msg = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var meta = new OutboxMessageMetadata(null, null, null);
        var result = await publisher.PublishRawAsync(msg, meta, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeTrue();
        result.Error.Should().BeSameAs(expectedEx);
    }
}





