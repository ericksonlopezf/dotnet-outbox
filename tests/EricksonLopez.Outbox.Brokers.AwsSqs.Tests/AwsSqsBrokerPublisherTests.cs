using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Amazon.SQS;
using Amazon.SQS.Model;
using AwesomeAssertions;
using EricksonLopez.Outbox.Brokers.AwsSqs;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Serialization;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Brokers;

public class AwsSqsBrokerPublisherTests
{
    [Fact]
    public async Task PublishAsync_Should_Succeed()
    {
        var sqs = Substitute.For<IAmazonSQS>();
        var serializer = Substitute.For<IOutboxSerializer>();
        serializer.Serialize("data").Returns(new byte[] { 1, 2, 3 });

        var publisher = new AwsSqsBrokerPublisher(sqs, serializer, "http://localhost/queue.fifo");

        var msg = new MessageEnvelope<string>("data", new MessageMetadata("corr", "caus", "type", new[] { new MetadataEntry("k", "v") }));
        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeTrue();
        await sqs.Received(1).SendMessageAsync(Arg.Is<SendMessageRequest>(r => 
            r.QueueUrl == "http://localhost/queue.fifo" &&
            r.MessageGroupId == "corr" &&
            r.MessageDeduplicationId == "caus" &&
            r.MessageAttributes["CorrelationId"].StringValue == "corr" &&
            r.MessageAttributes["MessageType"].StringValue == "type" &&
            r.MessageAttributes["k"].StringValue == "v"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_Should_Fail_And_Retry_On_HttpError()
    {
        var sqs = Substitute.For<IAmazonSQS>();
        var ex = new AmazonSQSException("test") { StatusCode = System.Net.HttpStatusCode.InternalServerError };
        sqs.SendMessageAsync(Arg.Any<SendMessageRequest>(), Arg.Any<CancellationToken>()).ThrowsAsync(ex);
        
        var serializer = Substitute.For<IOutboxSerializer>();
        var publisher = new AwsSqsBrokerPublisher(sqs, serializer, "http://localhost/queue");

        var msg = new MessageEnvelope<string>("data", new MessageMetadata(null, null, null));
        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeTrue();
    }

    [Fact]
    public async Task PublishAsync_Should_Fail_And_Retry_On_TooManyRequests()
    {
        var sqs = Substitute.For<IAmazonSQS>();
        var ex = new AmazonSQSException("test") { StatusCode = System.Net.HttpStatusCode.TooManyRequests };
        sqs.SendMessageAsync(Arg.Any<SendMessageRequest>(), Arg.Any<CancellationToken>()).ThrowsAsync(ex);
        
        var serializer = Substitute.For<IOutboxSerializer>();
        var publisher = new AwsSqsBrokerPublisher(sqs, serializer, "http://localhost/queue");

        var msg = new MessageEnvelope<string>("data", new MessageMetadata(null, null, null));
        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeTrue();
    }

    [Fact]
    public async Task PublishAsync_Should_Fail_Fatal_On_Other_Errors()
    {
        var sqs = Substitute.For<IAmazonSQS>();
        sqs.SendMessageAsync(Arg.Any<SendMessageRequest>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("fatal"));
        
        var serializer = Substitute.For<IOutboxSerializer>();
        var publisher = new AwsSqsBrokerPublisher(sqs, serializer, "http://localhost/queue");

        var msg = new MessageEnvelope<string>("data", new MessageMetadata(null, null, null));
        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeFalse();
        result.Error.Should().NotBeNull();
    }

    [Fact]
    public async Task PublishBatchAsync_Should_Succeed()
    {
        var sqs = Substitute.For<IAmazonSQS>();
        sqs.SendMessageBatchAsync(Arg.Any<SendMessageBatchRequest>(), Arg.Any<CancellationToken>()).Returns(new SendMessageBatchResponse { Failed = new List<BatchResultErrorEntry>() });

        var serializer = Substitute.For<IOutboxSerializer>();
        serializer.Serialize("data").Returns(new byte[] { 1, 2, 3 });
        var publisher = new AwsSqsBrokerPublisher(sqs, serializer, "http://localhost/queue");

        var msg = new MessageEnvelope<string>("data", new MessageMetadata("corr", "caus", "type1", new[] { new MetadataEntry("k", "v") }));
        var result = await publisher.PublishBatchAsync(new[] { msg }, new DispatchContext(CancellationToken.None, 1));

        result.Count.Should().Be(1);
        result[0].Success.Should().BeTrue();
        await sqs.Received(1).SendMessageBatchAsync(Arg.Is<SendMessageBatchRequest>(r => 
            r.QueueUrl == "http://localhost/queue" &&
            r.Entries.Count == 1 &&
            r.Entries[0].MessageBody != null &&
            r.Entries[0].MessageAttributes.ContainsKey("MessageType")), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishBatchAsync_Should_Fail_And_Retry_On_TooManyRequests()
    {
        var sqs = Substitute.For<IAmazonSQS>();
        var ex = new AmazonSQSException("rate limit") { StatusCode = System.Net.HttpStatusCode.TooManyRequests };
        sqs.SendMessageBatchAsync(Arg.Any<SendMessageBatchRequest>(), Arg.Any<CancellationToken>()).ThrowsAsync(ex);

        var serializer = Substitute.For<IOutboxSerializer>();
        var publisher = new AwsSqsBrokerPublisher(sqs, serializer, "http://localhost/queue");

        var msg = new MessageEnvelope<string>("data", new MessageMetadata(null, null, null));
        var result = await publisher.PublishBatchAsync(new[] { msg }, new DispatchContext(CancellationToken.None, 1));

        result.Count.Should().Be(1);
        result[0].Success.Should().BeFalse();
        result[0].ShouldRetry.Should().BeTrue();
    }

    [Fact]
    public async Task PublishBatchAsync_Should_Fail_When_Failed_Entries()
    {
        var sqs = Substitute.For<IAmazonSQS>();
        sqs.SendMessageBatchAsync(Arg.Any<SendMessageBatchRequest>(), Arg.Any<CancellationToken>()).Returns(new SendMessageBatchResponse { Failed = new List<BatchResultErrorEntry> { new BatchResultErrorEntry() } });

        var serializer = Substitute.For<IOutboxSerializer>();
        var publisher = new AwsSqsBrokerPublisher(sqs, serializer, "http://localhost/queue");

        var msg = new MessageEnvelope<string>("data", new MessageMetadata(null, null, null));
        var result = await publisher.PublishBatchAsync(new[] { msg }, new DispatchContext(CancellationToken.None, 1));

        result.Count.Should().Be(1);
        result[0].Success.Should().BeFalse();
    }

    [Fact]
    public async Task PublishRawAsync_Should_Succeed()
    {
        var sqs = Substitute.For<IAmazonSQS>();
        var serializer = Substitute.For<IOutboxSerializer>();

        var publisher = new AwsSqsBrokerPublisher(sqs, serializer, "http://localhost/queue");

        var msg = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var meta = new MessageMetadata("corr", "caus", "type", new[] { new MetadataEntry("k", "v") });
        var result = await publisher.PublishRawAsync(msg, meta, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeTrue();
        await sqs.Received(1).SendMessageAsync(Arg.Is<SendMessageRequest>(r =>
            r.QueueUrl == "http://localhost/queue" &&
            r.MessageAttributes["MessageType"].StringValue == "alias" &&
            r.MessageAttributes["CorrelationId"].StringValue == "corr" &&
            r.MessageAttributes["k"].StringValue == "v"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishRawAsync_Should_Fail()
    {
        var sqs = Substitute.For<IAmazonSQS>();
        sqs.SendMessageAsync(Arg.Any<SendMessageRequest>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("test"));
        var serializer = Substitute.For<IOutboxSerializer>();

        var publisher = new AwsSqsBrokerPublisher(sqs, serializer, "http://localhost/queue");

        var msg = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var meta = new MessageMetadata(null, null, null);
        var result = await publisher.PublishRawAsync(msg, meta, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeTrue();
    }
}



