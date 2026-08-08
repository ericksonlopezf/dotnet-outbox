using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Confluent.Kafka;
using AwesomeAssertions;
using EricksonLopez.Outbox.Brokers.Kafka;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Serialization;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;
#pragma warning disable CA2012 // NSubstitute generates ValueTasks that aren't awaited in Returns()

namespace EricksonLopez.Outbox.Tests.Brokers;

public class KafkaBrokerPublisherTests
{
    [Fact]
    public async Task PublishAsync_Should_Succeed()
    {
        var producer = Substitute.For<IProducer<byte[], byte[]>>();
        var serializer = Substitute.For<IOutboxSerializer>();
        serializer.Serialize("data").Returns(new byte[] { 1, 2, 3 });

        var publisher = new KafkaBrokerPublisher(producer, serializer, "topic");

        var msg = new MessageEnvelope<string>("data", new EricksonLopez.Outbox.MessageMetadata("corr", "caus", "type", new[] { new MetadataEntry("k", "v") }));
        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeTrue();
        await producer.Received(1).ProduceAsync("topic", Arg.Is<Message<byte[], byte[]>>(m =>
            m.Headers != null &&
            System.Text.Encoding.UTF8.GetString(m.Headers.GetLastBytes("CorrelationId")) == "corr" &&
            System.Text.Encoding.UTF8.GetString(m.Headers.GetLastBytes("CausationId")) == "caus" &&
            System.Text.Encoding.UTF8.GetString(m.Headers.GetLastBytes("MessageType")) == "type" &&
            System.Text.Encoding.UTF8.GetString(m.Headers.GetLastBytes("k")) == "v" &&
            System.Text.Encoding.UTF8.GetString(m.Key) == "corr"
        ), Arg.Any<CancellationToken>());
    }
    [Fact]
    public async Task PublishAsync_Should_Use_Overrides_For_Topic_And_PartitionKey()
    {
        var producer = Substitute.For<IProducer<byte[], byte[]>>();
        var serializer = Substitute.For<IOutboxSerializer>();
        serializer.Serialize("data").Returns(new byte[] { 1, 2, 3 });

        var publisher = new KafkaBrokerPublisher(producer, serializer, "default_topic");

        var msg = new MessageEnvelope<string>("data", new EricksonLopez.Outbox.MessageMetadata("corr", "caus", "type", new[] 
        { 
            new MetadataEntry("Kafka-Partition-Key", "my_partition"),
            new MetadataEntry("Kafka-Topic", "my_topic") 
        }));
        
        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));
        result.Success.Should().BeTrue();
        
        await producer.Received(1).ProduceAsync("my_topic", Arg.Is<Message<byte[], byte[]>>(m =>
            m.Key != null && System.Text.Encoding.UTF8.GetString(m.Key) == "my_partition"
        ), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishRawAsync_Should_Use_Overrides_For_Topic()
    {
        var producer = Substitute.For<IProducer<byte[], byte[]>>();
        var serializer = Substitute.For<IOutboxSerializer>();
        var publisher = new KafkaBrokerPublisher(producer, serializer, "default_topic");

        var msg = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var meta = new EricksonLopez.Outbox.MessageMetadata("corr", "caus", "type", new[] 
        { 
            new MetadataEntry("Kafka-Topic", "my_topic") 
        });

        var result = await publisher.PublishRawAsync(msg, meta, new DispatchContext(CancellationToken.None, 1));
        result.Success.Should().BeTrue();
        
        await producer.Received(1).ProduceAsync("my_topic", Arg.Any<Message<byte[], byte[]>>(), Arg.Any<CancellationToken>());
    }
    [Fact]
    public async Task PublishAsync_Should_Fail_And_Retry_On_ProduceException()
    {
        var producer = Substitute.For<IProducer<byte[], byte[]>>();
        var ex = new ProduceException<byte[], byte[]>(new Error(ErrorCode.BrokerNotAvailable), null);
        producer.ProduceAsync(Arg.Any<string>(), Arg.Any<Message<byte[], byte[]>>(), Arg.Any<CancellationToken>()).ThrowsAsync(ex);
        
        var serializer = Substitute.For<IOutboxSerializer>();
        var publisher = new KafkaBrokerPublisher(producer, serializer, "topic");

        var msg = new MessageEnvelope<string>("data", new EricksonLopez.Outbox.MessageMetadata(null, null, null));
        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeTrue();
    }

    [Fact]
    public async Task PublishAsync_Should_Fail_Fatal_On_Other_Errors()
    {
        var producer = Substitute.For<IProducer<byte[], byte[]>>();
        producer.ProduceAsync(Arg.Any<string>(), Arg.Any<Message<byte[], byte[]>>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("fatal"));
        
        var serializer = Substitute.For<IOutboxSerializer>();
        var publisher = new KafkaBrokerPublisher(producer, serializer, "topic");

        var msg = new MessageEnvelope<string>("data", new EricksonLopez.Outbox.MessageMetadata(null, null, null));
        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeFalse();
        result.Error.Should().NotBeNull();
        
        // Also test synchronous exceptions from serializer
        serializer.Serialize("data").Throws(new InvalidOperationException("Sync exception"));
        var result2 = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));
        result2.Success.Should().BeFalse();
        result2.ShouldRetry.Should().BeFalse();
        result2.Error.Should().NotBeNull();
    }

    [Fact]
    public async Task PublishBatchAsync_Should_Succeed()
    {
        var producer = Substitute.For<IProducer<byte[], byte[]>>();
        var serializer = Substitute.For<IOutboxSerializer>();
        var publisher = new KafkaBrokerPublisher(producer, serializer, "topic");

        var msg = new MessageEnvelope<string>("data", new EricksonLopez.Outbox.MessageMetadata(null, null, null));
        var result = await publisher.PublishBatchAsync(new[] { msg }, new DispatchContext(CancellationToken.None, 1));

        result.Count.Should().Be(1);
        result[0].Success.Should().BeTrue();
        
        await producer.Received(1).ProduceAsync("topic", Arg.Is<Message<byte[], byte[]>>(m => m.Headers != null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishRawAsync_Should_Succeed()
    {
        var producer = Substitute.For<IProducer<byte[], byte[]>>();
        var serializer = Substitute.For<IOutboxSerializer>();

        var publisher = new KafkaBrokerPublisher(producer, serializer, "topic");

        var msg = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var meta = new EricksonLopez.Outbox.MessageMetadata("corr", "caus", "type", new[] { new MetadataEntry("k", "v") });
        var result = await publisher.PublishRawAsync(msg, meta, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeTrue();
        await producer.Received(1).ProduceAsync("topic", Arg.Is<Message<byte[], byte[]>>(m =>
            m.Headers != null &&
            System.Text.Encoding.UTF8.GetString(m.Headers.GetLastBytes("correlation_id")) == "corr" &&
            System.Text.Encoding.UTF8.GetString(m.Headers.GetLastBytes("message_type")) == "alias"
        ), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishRawAsync_Should_Fail()
    {
        var producer = Substitute.For<IProducer<byte[], byte[]>>();
        var ex = new ProduceException<byte[], byte[]>(new Error(ErrorCode.BrokerNotAvailable), null);
        producer.ProduceAsync(Arg.Any<string>(), Arg.Any<Message<byte[], byte[]>>(), Arg.Any<CancellationToken>()).ThrowsAsync(ex);
        
        var serializer = Substitute.For<IOutboxSerializer>();

        var publisher = new KafkaBrokerPublisher(producer, serializer, "topic");

        var msg = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var meta = new EricksonLopez.Outbox.MessageMetadata(null, null, null);
        var result = await publisher.PublishRawAsync(msg, meta, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeTrue();
    }

    [Fact]
    public async Task PublishRawAsync_Should_Fail_Fatal_On_Other_Errors()
    {
        var producer = Substitute.For<IProducer<byte[], byte[]>>();
        producer.ProduceAsync(Arg.Any<string>(), Arg.Any<Message<byte[], byte[]>>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("fatal"));
        
        var serializer = Substitute.For<IOutboxSerializer>();

        var publisher = new KafkaBrokerPublisher(producer, serializer, "topic");

        var msg = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var meta = new EricksonLopez.Outbox.MessageMetadata(null, null, null);
        var result = await publisher.PublishRawAsync(msg, meta, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeFalse();
        result.Error.Should().NotBeNull();

        // Also test synchronous exceptions
        producer.ProduceAsync(Arg.Any<string>(), Arg.Any<Message<byte[], byte[]>>(), Arg.Any<CancellationToken>()).Throws(new InvalidOperationException("Sync exception"));
        var result2 = await publisher.PublishRawAsync(msg, meta, new DispatchContext(CancellationToken.None, 1));
        result2.Success.Should().BeFalse();
        result2.ShouldRetry.Should().BeFalse();
        result2.Error.Should().NotBeNull();
    }
}



