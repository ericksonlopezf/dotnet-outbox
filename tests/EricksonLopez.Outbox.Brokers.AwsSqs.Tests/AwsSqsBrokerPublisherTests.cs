// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Amazon.SQS;
using Amazon.SQS.Model;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Brokers.AwsSqs;
using EricksonLopez.Outbox.Serialization;
using EricksonLopez.Result;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Brokers;

public class AwsSqsBrokerPublisherTests
{
    [Fact]
    public void Constructor_NullGuards()
    {
        var sqs = Substitute.For<IAmazonSQS>();
        var serializer = Substitute.For<IOutboxSerializer>();

        Action act1 = () => { _ = new AwsSqsBrokerPublisher(null!, serializer, "http://localhost/queue"); };
        act1.Should().Throw<ArgumentNullException>().WithParameterName("sqsClient");

        Action act2 = () => { _ = new AwsSqsBrokerPublisher(sqs, null!, "http://localhost/queue"); };
        act2.Should().Throw<ArgumentNullException>().WithParameterName("serializer");

        Action act3 = () => { _ = new AwsSqsBrokerPublisher(sqs, serializer, null!); };
        act3.Should().Throw<ArgumentNullException>().WithParameterName("queueUrl");
    }

    [Fact]
    public async Task PublishAsync_WhenFifoQueueAndNoMetadata_SetsDefaultGroupAndGeneratedDeduplicationId()
    {
        var sqs = Substitute.For<IAmazonSQS>();
        var serializer = Substitute.For<IOutboxSerializer>();
        serializer.Serialize("data").Returns(new byte[] { 1 });

        var publisher = new AwsSqsBrokerPublisher(sqs, serializer, "http://localhost/my-queue.fifo");
        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata(null, null, null));

        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));
        result.Success.Should().BeTrue();

        await sqs.Received(1).SendMessageAsync(Arg.Is<SendMessageRequest>(r =>
            r.QueueUrl == "http://localhost/my-queue.fifo" &&
            r.MessageGroupId == "default-group" &&
            !string.IsNullOrEmpty(r.MessageDeduplicationId) &&
            r.MessageDeduplicationId.Length > 0 &&
            !r.MessageAttributes.ContainsKey("CorrelationId") &&
            !r.MessageAttributes.ContainsKey("MessageType")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_WhenNonFifoQueue_DoesNotSetGroupIdOrDeduplicationId()
    {
        var sqs = Substitute.For<IAmazonSQS>();
        var serializer = Substitute.For<IOutboxSerializer>();
        serializer.Serialize("data").Returns(new byte[] { 1 });

        var publisher = new AwsSqsBrokerPublisher(sqs, serializer, "http://localhost/standard-queue");
        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata("c1", "ca1", "type1"));

        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));
        result.Success.Should().BeTrue();

        await sqs.Received(1).SendMessageAsync(Arg.Is<SendMessageRequest>(r =>
            r.QueueUrl == "http://localhost/standard-queue" &&
            r.MessageGroupId == null &&
            r.MessageDeduplicationId == null &&
            r.MessageAttributes["CorrelationId"].StringValue == "c1" &&
            r.MessageAttributes["MessageType"].StringValue == "type1"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_WhenValidMessage_SendsToSqs()
    {
        var sqs = Substitute.For<IAmazonSQS>();
        var serializer = Substitute.For<IOutboxSerializer>();
        serializer.Serialize("data").Returns(new byte[] { 1, 2, 3 });

        var publisher = new AwsSqsBrokerPublisher(sqs, serializer, "http://localhost/queue.fifo");

        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata("corr", "caus", "type", new[] { new MetadataEntry("k", "v") }));
        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeTrue();
        await sqs.Received(1).SendMessageAsync(Arg.Is<SendMessageRequest>(r => 
            r.QueueUrl == "http://localhost/queue.fifo" &&
            r.MessageGroupId == "corr" &&
            r.MessageDeduplicationId == "caus" &&
            r.MessageAttributes["CorrelationId"].DataType == "String" &&
            r.MessageAttributes["CorrelationId"].StringValue == "corr" &&
            r.MessageAttributes["MessageType"].DataType == "String" &&
            r.MessageAttributes["MessageType"].StringValue == "type" &&
            r.MessageAttributes["k"].DataType == "String" &&
            r.MessageAttributes["k"].StringValue == "v"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_WhenHttpErrorOccurs_ReturnsFailAndRetry()
    {
        var sqs = Substitute.For<IAmazonSQS>();
        var ex = new AmazonSQSException("test") { StatusCode = System.Net.HttpStatusCode.InternalServerError };
        sqs.SendMessageAsync(Arg.Any<SendMessageRequest>(), Arg.Any<CancellationToken>()).ThrowsAsync(ex);
        
        var serializer = Substitute.For<IOutboxSerializer>();
        var publisher = new AwsSqsBrokerPublisher(sqs, serializer, "http://localhost/queue");

        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata(null, null, null));
        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeTrue();
    }

    [Fact]
    public async Task PublishAsync_WhenTooManyRequests_ReturnsFailAndRetry()
    {
        var sqs = Substitute.For<IAmazonSQS>();
        var ex = new AmazonSQSException("test") { StatusCode = System.Net.HttpStatusCode.TooManyRequests };
        sqs.SendMessageAsync(Arg.Any<SendMessageRequest>(), Arg.Any<CancellationToken>()).ThrowsAsync(ex);
        
        var serializer = Substitute.For<IOutboxSerializer>();
        var publisher = new AwsSqsBrokerPublisher(sqs, serializer, "http://localhost/queue");

        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata(null, null, null));
        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeTrue();
    }

    [Fact]
    public async Task PublishAsync_WhenFatalErrorOccurs_ReturnsFailWithoutRetry()
    {
        var sqs = Substitute.For<IAmazonSQS>();
        sqs.SendMessageAsync(Arg.Any<SendMessageRequest>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("fatal"));
        
        var serializer = Substitute.For<IOutboxSerializer>();
        var publisher = new AwsSqsBrokerPublisher(sqs, serializer, "http://localhost/queue");

        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata(null, null, null));
        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeFalse();
        result.Error.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Be("fatal");
    }

    [Fact]
    public async Task PublishBatchAsync_WhenValidBatch_SendsBatchToSqs()
    {
        var sqs = Substitute.For<IAmazonSQS>();
        sqs.SendMessageBatchAsync(Arg.Any<SendMessageBatchRequest>(), Arg.Any<CancellationToken>()).Returns(new SendMessageBatchResponse { Failed = new List<BatchResultErrorEntry>() });

        var serializer = Substitute.For<IOutboxSerializer>();
        serializer.Serialize("data").Returns(new byte[] { 1, 2, 3 });
        var publisher = new AwsSqsBrokerPublisher(sqs, serializer, "http://localhost/queue");

        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata("corr", "caus", "type1", new[] { new MetadataEntry("k", "v") }));
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
    public async Task PublishBatchAsync_WhenTooManyRequests_ReturnsFailAndRetry()
    {
        var sqs = Substitute.For<IAmazonSQS>();
        var ex = new AmazonSQSException("rate limit") { StatusCode = System.Net.HttpStatusCode.TooManyRequests };
        sqs.SendMessageBatchAsync(Arg.Any<SendMessageBatchRequest>(), Arg.Any<CancellationToken>()).ThrowsAsync(ex);

        var serializer = Substitute.For<IOutboxSerializer>();
        var publisher = new AwsSqsBrokerPublisher(sqs, serializer, "http://localhost/queue");

        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata(null, null, null));
        var result = await publisher.PublishBatchAsync(new[] { msg }, new DispatchContext(CancellationToken.None, 1));

        result.Count.Should().Be(1);
        result[0].Success.Should().BeFalse();
        result[0].ShouldRetry.Should().BeTrue();
    }

    [Fact]
    public async Task PublishBatchAsync_WhenBatchHasFailedEntries_ReturnsFailure()
    {
        var sqs = Substitute.For<IAmazonSQS>();
        sqs.SendMessageBatchAsync(Arg.Any<SendMessageBatchRequest>(), Arg.Any<CancellationToken>()).Returns(new SendMessageBatchResponse { Failed = new List<BatchResultErrorEntry> { new BatchResultErrorEntry() } });

        var serializer = Substitute.For<IOutboxSerializer>();
        var publisher = new AwsSqsBrokerPublisher(sqs, serializer, "http://localhost/queue");

        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata(null, null, null));
        var result = await publisher.PublishBatchAsync(new[] { msg }, new DispatchContext(CancellationToken.None, 1));

        result.Count.Should().Be(1);
        result[0].Success.Should().BeFalse();
        result[0].ShouldRetry.Should().BeTrue();
        result[0].Error.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Be("SQS Batch Send failed for 1 messages.");
    }

    [Fact]
    public async Task PublishRawAsync_WhenValidMessage_SendsToSqs()
    {
        var sqs = Substitute.For<IAmazonSQS>();
        var serializer = Substitute.For<IOutboxSerializer>();

        var publisher = new AwsSqsBrokerPublisher(sqs, serializer, "http://localhost/queue");

        var msg = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var meta = new OutboxMessageMetadata("corr", "caus", "type", new[] { new MetadataEntry("k", "v") });
        var result = await publisher.PublishRawAsync(msg, meta, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeTrue();
        await sqs.Received(1).SendMessageAsync(Arg.Is<SendMessageRequest>(r =>
            r.QueueUrl == "http://localhost/queue" &&
            r.MessageAttributes["MessageType"].DataType == "String" &&
            r.MessageAttributes["MessageType"].StringValue == "alias" &&
            r.MessageAttributes["CorrelationId"].DataType == "String" &&
            r.MessageAttributes["CorrelationId"].StringValue == "corr" &&
            r.MessageAttributes["k"].DataType == "String" &&
            r.MessageAttributes["k"].StringValue == "v"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishRawAsync_WhenExceptionThrown_ReturnsFailAndRetry()
    {
        var sqs = Substitute.For<IAmazonSQS>();
        sqs.SendMessageAsync(Arg.Any<SendMessageRequest>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("test"));
        var serializer = Substitute.For<IOutboxSerializer>();

        var publisher = new AwsSqsBrokerPublisher(sqs, serializer, "http://localhost/queue");

        var msg = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var meta = new OutboxMessageMetadata(null, null, null);
        var result = await publisher.PublishRawAsync(msg, meta, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeTrue();
    }
}








