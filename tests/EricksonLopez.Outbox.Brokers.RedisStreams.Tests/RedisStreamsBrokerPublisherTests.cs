// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Brokers.RedisStreams;
using EricksonLopez.Result;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using StackExchange.Redis;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Brokers;

public class RedisStreamsBrokerPublisherTests
{
    [Fact]
    public void Constructor_NullGuards()
    {
        Action act = () => { _ = new RedisStreamsBrokerPublisher(null!); };
        act.Should().Throw<ArgumentNullException>().WithParameterName("redis");
    }

    [Fact]
    public async Task Constructor_CustomMaxStreamLength_PassesToRedis()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(db);

        var publisher = new RedisStreamsBrokerPublisher(redis, maxStreamLength: 500);
        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata("corr", "caus", "type1"));

        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));
        result.Success.Should().BeTrue();

        await db.Received(1).StreamAddAsync(
            "outbox:type1",
            Arg.Any<NameValueEntry[]>(),
            null,
            500,
            true);
    }

    [Fact]
    public async Task PublishAsync_WhenMessageTypeIsNull_FallsBackToTypeName()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(db);

        var publisher = new RedisStreamsBrokerPublisher(redis);
        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata(null, null, null));

        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));
        result.Success.Should().BeTrue();

        await db.Received(1).StreamAddAsync(
            "outbox:string",
            Arg.Is<NameValueEntry[]>(e =>
                e.Any(x => x.Name == "payload") &&
                e.Any(x => x.Name == "message_type" && x.Value == string.Empty) &&
                !e.Any(x => x.Name == "correlation_id") &&
                !e.Any(x => x.Name == "causation_id")),
            null,
            10000,
            true);
    }

    [Fact]
    public async Task PublishAsync_WithDotsAndUpperCase_FormatsStreamKeyCorrectly()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(db);

        var publisher = new RedisStreamsBrokerPublisher(redis);
        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata("c", "caus", "Order.Payment.Completed.V1"));

        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));
        result.Success.Should().BeTrue();

        await db.Received(1).StreamAddAsync(
            "outbox:order:payment:completed:v1",
            Arg.Any<NameValueEntry[]>(),
            null,
            10000,
            true);
    }

    [Fact]
    public async Task PublishAsync_Should_AddStreamEntryToRedis_WhenMessageIsValid()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(db);
        
        var publisher = new RedisStreamsBrokerPublisher(redis);

        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata("corr", "caus", "type1", new[] { new MetadataEntry("k", "v") }));
        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeTrue();
        await db.Received(1).StreamAddAsync("outbox:type1", Arg.Is<NameValueEntry[]>(e => 
            e.Length > 0 && 
            Enumerable.Any(e, x => x.Name == "correlation_id" && x.Value == "corr") &&
            Enumerable.Any(e, x => x.Name == "causation_id" && x.Value == "caus") &&
            Enumerable.Any(e, x => x.Name == "message_type" && x.Value == "type1") &&
            Enumerable.Any(e, x => x.Name == "k" && x.Value == "v")
        ), null, 10000, true);
    }

    [Fact]
    public async Task PublishAsync_Should_ReturnFailAndRetry_WhenRedisThrowsException()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(db);
        var expectedEx = new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Redis offline");
        db.StreamAddAsync(Arg.Any<RedisKey>(), Arg.Any<NameValueEntry[]>(), Arg.Any<RedisValue?>(), Arg.Any<int?>(), Arg.Any<bool>(), Arg.Any<CommandFlags>()).ThrowsAsync(expectedEx);

        var publisher = new RedisStreamsBrokerPublisher(redis);

        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata(null, null, null));
        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeTrue();
        result.Error.Should().BeSameAs(expectedEx);
    }

    [Fact]
    public async Task PublishBatchAsync_Should_PublishAllMessagesToRedisStream()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(db);
        
        var publisher = new RedisStreamsBrokerPublisher(redis);

        var msg1 = new MessageEnvelope<string>("data1", new OutboxMessageMetadata("c1", null, "t1"));
        var msg2 = new MessageEnvelope<string>("data2", new OutboxMessageMetadata("c2", null, "t2"));
        var result = await publisher.PublishBatchAsync(new[] { msg1, msg2 }, new DispatchContext(CancellationToken.None, 1));

        result.Count.Should().Be(2);
        result[0].Success.Should().BeTrue();
        result[1].Success.Should().BeTrue();

        await db.Received(1).StreamAddAsync("outbox:t1", Arg.Any<NameValueEntry[]>(), null, 10000, true);
        await db.Received(1).StreamAddAsync("outbox:t2", Arg.Any<NameValueEntry[]>(), null, 10000, true);
    }

    [Fact]
    public async Task PublishRawAsync_Should_AddStreamEntryToRedis_WithRawPayload()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(db);
        
        var publisher = new RedisStreamsBrokerPublisher(redis);

        var msg = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var meta = new OutboxMessageMetadata("corr", "caus", "type1", new[] { new MetadataEntry("k", "v") });
        var result = await publisher.PublishRawAsync(msg, meta, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeTrue();
        await db.Received(1).StreamAddAsync("outbox:alias", Arg.Is<NameValueEntry[]>(e => 
            e.Length > 0 && 
            Enumerable.Any(e, x => x.Name == "correlation_id" && x.Value == "corr") &&
            Enumerable.Any(e, x => x.Name == "causation_id" && x.Value == "caus") &&
            Enumerable.Any(e, x => x.Name == "message_type" && x.Value == "type1") &&
            Enumerable.Any(e, x => x.Name == "k" && x.Value == "v")
        ), null, 10000, true);
    }

    [Fact]
    public async Task PublishRawAsync_Should_ReturnFailAndRetry_WhenRedisThrowsException()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(db);
        var expectedEx = new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Redis offline");
        db.StreamAddAsync(Arg.Any<RedisKey>(), Arg.Any<NameValueEntry[]>(), Arg.Any<RedisValue?>(), Arg.Any<int?>(), Arg.Any<bool>(), Arg.Any<CommandFlags>()).ThrowsAsync(expectedEx);
        
        var publisher = new RedisStreamsBrokerPublisher(redis);

        var msg = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var meta = new OutboxMessageMetadata(null, null, null);
        var result = await publisher.PublishRawAsync(msg, meta, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeTrue();
        result.Error.Should().BeSameAs(expectedEx);
    }
}






