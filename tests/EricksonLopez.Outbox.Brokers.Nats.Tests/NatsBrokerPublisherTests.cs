using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NATS.Client.Core;
using AwesomeAssertions;
using EricksonLopez.Outbox.Brokers.Nats;
using EricksonLopez.Outbox;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;
#pragma warning disable CA2012 // NSubstitute generates ValueTasks that aren't awaited in Returns()

namespace EricksonLopez.Outbox.Tests.Brokers;

public class NatsBrokerPublisherTests
{
    [Fact]
    public async Task PublishAsync_Should_Succeed()
    {
        var connection = Substitute.For<INatsConnection>();
        var publisher = new NatsBrokerPublisher(connection);

        var msg = new MessageEnvelope<string>("data", new MessageMetadata("corr", "caus", "type", new[] { new MetadataEntry("k", "v") }));
        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeTrue();
        await connection.Received(1).PublishAsync("type", Arg.Any<MessageEnvelope<string>>(), Arg.Is<NatsHeaders>(h => 
            h.ContainsKey("X-Correlation-Id") && h["X-Correlation-Id"].ToString() == "corr" &&
            h.ContainsKey("X-Causation-Id") && h["X-Causation-Id"].ToString() == "caus" &&
            h.ContainsKey("k") && h["k"].ToString() == "v"
        ), Arg.Any<string>(), Arg.Any<INatsSerialize<MessageEnvelope<string>>>(), Arg.Any<NatsPubOpts>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_Should_Fail()
    {
        var connection = Substitute.For<INatsConnection>();
        _ = connection.PublishAsync(Arg.Any<string>(), Arg.Any<MessageEnvelope<string>>(), Arg.Any<NatsHeaders>(), Arg.Any<string>(), Arg.Any<INatsSerialize<MessageEnvelope<string>>>(), Arg.Any<NatsPubOpts>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromException(new InvalidOperationException("test")));
        var publisher = new NatsBrokerPublisher(connection);

        var msg = new MessageEnvelope<string>("data", new MessageMetadata(null, null, null));
        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeTrue();
    }

    [Fact]
    public async Task PublishBatchAsync_Should_Succeed()
    {
        var connection = Substitute.For<INatsConnection>();
        var publisher = new NatsBrokerPublisher(connection);

        var msg = new MessageEnvelope<string>("data", new MessageMetadata(null, null, null));
        var result = await publisher.PublishBatchAsync(new[] { msg }, new DispatchContext(CancellationToken.None, 1));

        result.Count.Should().Be(1);
        result[0].Success.Should().BeTrue();
    }

    [Fact]
    public async Task PublishRawAsync_Should_Succeed()
    {
        var connection = Substitute.For<INatsConnection>();
        var publisher = new NatsBrokerPublisher(connection);

        var msg = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var meta = new MessageMetadata("corr", "caus", "type", new[] { new MetadataEntry("k", "v") });
        var result = await publisher.PublishRawAsync(msg, meta, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeTrue();
        await connection.Received(1).PublishAsync("alias", Arg.Any<byte[]>(), Arg.Is<NatsHeaders>(h => 
            h.ContainsKey("X-Correlation-Id") && h["X-Correlation-Id"].ToString() == "corr" &&
            h.ContainsKey("X-Causation-Id") && h["X-Causation-Id"].ToString() == "caus" &&
            h.ContainsKey("k") && h["k"].ToString() == "v"
        ), Arg.Any<string>(), Arg.Any<INatsSerialize<byte[]>>(), Arg.Any<NatsPubOpts>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishRawAsync_Should_Fail()
    {
        var connection = Substitute.For<INatsConnection>();
        _ = connection.PublishAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<NatsHeaders>(), Arg.Any<string>(), Arg.Any<INatsSerialize<byte[]>>(), Arg.Any<NatsPubOpts>(), Arg.Any<CancellationToken>()).Returns(ValueTask.FromException(new InvalidOperationException("test")));
        
        var publisher = new NatsBrokerPublisher(connection);

        var msg = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var meta = new MessageMetadata(null, null, null);
        var result = await publisher.PublishRawAsync(msg, meta, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeTrue();
    }
}



