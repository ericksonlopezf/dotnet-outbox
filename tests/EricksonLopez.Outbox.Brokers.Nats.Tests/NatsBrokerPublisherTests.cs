// Copyright © Erickson Lopez. MIT License.
#pragma warning disable CA2012 // NSubstitute generates ValueTasks that aren't awaited in Returns()
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Brokers.Nats;
using EricksonLopez.Result;
using NATS.Client.Core;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Brokers;

public class NatsBrokerPublisherTests
{
    [Fact]
    public void Constructor_NullGuards()
    {
        Action act = () => { _ = new NatsBrokerPublisher(null!); };
        act.Should().Throw<ArgumentNullException>().WithParameterName("connection");
    }

    [Fact]
    public async Task PublishAsync_WhenMessageTypeIsNull_FallsBackToTypeName()
    {
        var connection = Substitute.For<INatsConnection>();
        var publisher = new NatsBrokerPublisher(connection);

        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata(null, null, null));
        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeTrue();
        await connection.Received(1).PublishAsync(
            "String",
            Arg.Any<MessageEnvelope<string>>(),
            Arg.Is<NatsHeaders>(h =>
                !h.ContainsKey("X-Correlation-Id") &&
                !h.ContainsKey("X-Causation-Id") &&
                h.Count == 0),
            Arg.Any<string>(),
            Arg.Any<INatsSerialize<MessageEnvelope<string>>>(),
            Arg.Any<NatsPubOpts>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_Should_PublishToNats_WhenMessageIsValid()
    {
        var connection = Substitute.For<INatsConnection>();
        var publisher = new NatsBrokerPublisher(connection);

        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata("corr", "caus", "type", new[] { new MetadataEntry("k", "v") }));
        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeTrue();
        await connection.Received(1).PublishAsync("type", Arg.Any<MessageEnvelope<string>>(), Arg.Is<NatsHeaders>(h => 
            h.ContainsKey("X-Correlation-Id") && h["X-Correlation-Id"].ToString() == "corr" &&
            h.ContainsKey("X-Causation-Id") && h["X-Causation-Id"].ToString() == "caus" &&
            h.ContainsKey("k") && h["k"].ToString() == "v"
        ), Arg.Any<string>(), Arg.Any<INatsSerialize<MessageEnvelope<string>>>(), Arg.Any<NatsPubOpts>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_Should_PropagateCancellationToken_ToNats()
    {
        var connection = Substitute.For<INatsConnection>();
        var publisher = new NatsBrokerPublisher(connection);

        using var cts = new CancellationTokenSource();
        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata("corr", "caus", "type", null));
        var result = await publisher.PublishAsync(msg, new DispatchContext(cts.Token, 1));

        result.Success.Should().BeTrue();
        await connection.Received(1).PublishAsync(
            "type",
            Arg.Any<MessageEnvelope<string>>(),
            Arg.Any<NatsHeaders>(),
            Arg.Any<string>(),
            Arg.Any<INatsSerialize<MessageEnvelope<string>>>(),
            Arg.Any<NatsPubOpts>(),
            cts.Token);
    }

    [Fact]
    public async Task PublishAsync_Should_ReturnFailAndRetry_WhenConnectionThrowsException()
    {
        var connection = Substitute.For<INatsConnection>();
        var expectedEx = new InvalidOperationException("NATS server unavailable");
        _ = connection.PublishAsync(Arg.Any<string>(), Arg.Any<MessageEnvelope<string>>(), Arg.Any<NatsHeaders>(), Arg.Any<string>(), Arg.Any<INatsSerialize<MessageEnvelope<string>>>(), Arg.Any<NatsPubOpts>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromException(expectedEx));
        var publisher = new NatsBrokerPublisher(connection);

        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata(null, null, null));
        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeTrue();
        result.Error.Should().BeSameAs(expectedEx);
    }

    [Fact]
    public async Task PublishBatchAsync_Should_PublishAllMessagesToNats()
    {
        var connection = Substitute.For<INatsConnection>();
        var publisher = new NatsBrokerPublisher(connection);

        var msg1 = new MessageEnvelope<string>("data1", new OutboxMessageMetadata("c1", null, "t1"));
        var msg2 = new MessageEnvelope<string>("data2", new OutboxMessageMetadata("c2", null, "t2"));
        var result = await publisher.PublishBatchAsync(new[] { msg1, msg2 }, new DispatchContext(CancellationToken.None, 1));

        result.Count.Should().Be(2);
        result[0].Success.Should().BeTrue();
        result[1].Success.Should().BeTrue();

        await connection.Received(1).PublishAsync("t1", Arg.Any<MessageEnvelope<string>>(), Arg.Any<NatsHeaders>(), Arg.Any<string>(), Arg.Any<INatsSerialize<MessageEnvelope<string>>>(), Arg.Any<NatsPubOpts>(), Arg.Any<CancellationToken>());
        await connection.Received(1).PublishAsync("t2", Arg.Any<MessageEnvelope<string>>(), Arg.Any<NatsHeaders>(), Arg.Any<string>(), Arg.Any<INatsSerialize<MessageEnvelope<string>>>(), Arg.Any<NatsPubOpts>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishRawAsync_Should_PublishRawPayloadToNats()
    {
        var connection = Substitute.For<INatsConnection>();
        var publisher = new NatsBrokerPublisher(connection);

        var msg = new OutboxMessage(Guid.NewGuid(), "alias", new byte[] { 1, 2, 3 }, null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var meta = new OutboxMessageMetadata("corr", "caus", "type", new[] { new MetadataEntry("k", "v") });
        var result = await publisher.PublishRawAsync(msg, meta, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeTrue();
        await connection.Received(1).PublishAsync("alias", Arg.Is<byte[]>(b => b.Length == 3), Arg.Is<NatsHeaders>(h => 
            h.ContainsKey("X-Correlation-Id") && h["X-Correlation-Id"].ToString() == "corr" &&
            h.ContainsKey("X-Causation-Id") && h["X-Causation-Id"].ToString() == "caus" &&
            h.ContainsKey("k") && h["k"].ToString() == "v"
        ), Arg.Any<string>(), Arg.Any<INatsSerialize<byte[]>>(), Arg.Any<NatsPubOpts>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishRawAsync_Should_ReturnFailAndRetry_WhenConnectionThrowsException()
    {
        var connection = Substitute.For<INatsConnection>();
        var expectedEx = new InvalidOperationException("NATS server unavailable");
        _ = connection.PublishAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<NatsHeaders>(), Arg.Any<string>(), Arg.Any<INatsSerialize<byte[]>>(), Arg.Any<NatsPubOpts>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromException(expectedEx));
        
        var publisher = new NatsBrokerPublisher(connection);

        var msg = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var meta = new OutboxMessageMetadata(null, null, null);
        var result = await publisher.PublishRawAsync(msg, meta, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeTrue();
        result.Error.Should().BeSameAs(expectedEx);
    }
}





