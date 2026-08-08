using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using StackExchange.Redis;
using AwesomeAssertions;
using EricksonLopez.Outbox.Brokers.RedisStreams;
using EricksonLopez.Outbox;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Brokers;

public class RedisStreamsBrokerPublisherTests
{
    [Fact]
    public async Task PublishAsync_Should_Succeed()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(db);
        
        var publisher = new RedisStreamsBrokerPublisher(redis);

        var msg = new MessageEnvelope<string>("data", new MessageMetadata("corr", "caus", "type1", new[] { new MetadataEntry("k", "v") }));
        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeTrue();
        await db.Received(1).StreamAddAsync("outbox:type1", Arg.Is<NameValueEntry[]>(e => 
            e.Length > 0 && 
            System.Linq.Enumerable.Any(e, x => x.Name == "correlation_id" && x.Value == "corr") &&
            System.Linq.Enumerable.Any(e, x => x.Name == "causation_id" && x.Value == "caus") &&
            System.Linq.Enumerable.Any(e, x => x.Name == "message_type" && x.Value == "type1") &&
            System.Linq.Enumerable.Any(e, x => x.Name == "k" && x.Value == "v")
        ), null, 10000, true);
    }

    [Fact]
    public async Task PublishAsync_Should_Fail()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(db);
        db.StreamAddAsync(Arg.Any<RedisKey>(), Arg.Any<NameValueEntry[]>(), Arg.Any<RedisValue?>(), Arg.Any<int?>(), Arg.Any<bool>(), Arg.Any<CommandFlags>()).ThrowsAsync(new InvalidOperationException("test"));

        var publisher = new RedisStreamsBrokerPublisher(redis);

        var msg = new MessageEnvelope<string>("data", new MessageMetadata(null, null, null));
        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeTrue();
    }

    [Fact]
    public async Task PublishBatchAsync_Should_Succeed()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(db);
        
        var publisher = new RedisStreamsBrokerPublisher(redis);

        var msg = new MessageEnvelope<string>("data", new MessageMetadata(null, null, null));
        var result = await publisher.PublishBatchAsync(new[] { msg }, new DispatchContext(CancellationToken.None, 1));

        result.Count.Should().Be(1);
        result[0].Success.Should().BeTrue();
    }

    [Fact]
    public async Task PublishRawAsync_Should_Succeed()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(db);
        
        var publisher = new RedisStreamsBrokerPublisher(redis);

        var msg = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var meta = new MessageMetadata("corr", "caus", "type1", new[] { new MetadataEntry("k", "v") });
        var result = await publisher.PublishRawAsync(msg, meta, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeTrue();
        await db.Received(1).StreamAddAsync("outbox:alias", Arg.Is<NameValueEntry[]>(e => 
            e.Length > 0 && 
            System.Linq.Enumerable.Any(e, x => x.Name == "correlation_id" && x.Value == "corr") &&
            System.Linq.Enumerable.Any(e, x => x.Name == "causation_id" && x.Value == "caus") &&
            System.Linq.Enumerable.Any(e, x => x.Name == "message_type" && x.Value == "type1") &&
            System.Linq.Enumerable.Any(e, x => x.Name == "k" && x.Value == "v")
        ), null, 10000, true);
    }

    [Fact]
    public async Task PublishRawAsync_Should_Fail()
    {
        var redis = Substitute.For<IConnectionMultiplexer>();
        var db = Substitute.For<IDatabase>();
        redis.GetDatabase(Arg.Any<int>(), Arg.Any<object>()).Returns(db);
        db.StreamAddAsync(Arg.Any<RedisKey>(), Arg.Any<NameValueEntry[]>(), Arg.Any<RedisValue?>(), Arg.Any<int?>(), Arg.Any<bool>(), Arg.Any<CommandFlags>()).ThrowsAsync(new InvalidOperationException("test"));
        
        var publisher = new RedisStreamsBrokerPublisher(redis);

        var msg = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var meta = new MessageMetadata(null, null, null);
        var result = await publisher.PublishRawAsync(msg, meta, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeTrue();
    }
}



