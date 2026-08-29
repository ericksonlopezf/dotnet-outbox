// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Producer;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Brokers.AzureEventHubs;
using EricksonLopez.Outbox.Serialization;
using EricksonLopez.Result;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Brokers.AzureEventHubs;

public class AzureEventHubsBrokerPublisherTests
{
    private readonly IOutboxSerializer _serializer = Substitute.For<IOutboxSerializer>();

    [Fact]
    public void Constructor_With_Null_Producer_Throws_ArgumentNullException()
    {
        var act = () => new AzureEventHubsBrokerPublisher(null!, _serializer);
        act.Should().Throw<ArgumentNullException>().WithParameterName("producerClient");
    }

    [Fact]
    public void BrokerSystemName_Should_Return_azure_event_hubs()
    {
        var publisher = (IBrokerPublisher)new AzureEventHubsBrokerPublisher(
            Substitute.For<EventHubProducerClient>(), _serializer);

        publisher.BrokerSystemName.Should().Be("azure_event_hubs");
    }

    [Fact]
    public async Task PublishAsync_WhenSerializerIsNull_ReturnsFailFatal()
    {
        var client = Substitute.For<EventHubProducerClient>();
        var publisher = new AzureEventHubsBrokerPublisher(client, serializer: null);

        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata("corr", "caus", "type"));
        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeFalse();
        result.Error.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Contain("No IOutboxSerializer was provided");
    }

    [Fact]
    public async Task PublishAsync_WhenValid_SendsEventDataWithPropertiesAndPartitionKey()
    {
        var client = Substitute.For<EventHubProducerClient>();
        _serializer.Serialize("data").Returns(new byte[] { 1, 2, 3 });
        var publisher = new AzureEventHubsBrokerPublisher(client, _serializer);

        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata("corr-1", "caus-1", "OrderCreated", new[] { new MetadataEntry("k", "v") }));
        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeTrue();
        await client.Received(1).SendAsync(
            Arg.Is<IEnumerable<EventData>>(events =>
                events != null &&
                events.First().Properties["CorrelationId"].ToString() == "corr-1" &&
                events.First().Properties["CausationId"].ToString() == "caus-1" &&
                events.First().Properties["MessageType"].ToString() == "OrderCreated" &&
                events.First().Properties["k"].ToString() == "v"),
            Arg.Is<SendEventOptions>(opts => opts.PartitionKey == "corr-1"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_WhenTransientEventHubsException_ReturnsFailAndRetry()
    {
        var client = Substitute.For<EventHubProducerClient>();
        _serializer.Serialize("data").Returns(new byte[] { 1 });
        var publisher = new AzureEventHubsBrokerPublisher(client, _serializer);

        var ex = new EventHubsException(isTransient: true, "eventhub-name", "Server busy", EventHubsException.FailureReason.ServiceBusy);
        client.SendAsync(Arg.Any<IEnumerable<EventData>>(), Arg.Any<SendEventOptions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(ex);

        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata("corr", "caus", "type"));
        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeTrue();
        result.Error.Should().BeSameAs(ex);
    }

    [Fact]
    public async Task PublishAsync_WhenNonTransientException_ReturnsFailFatal()
    {
        var client = Substitute.For<EventHubProducerClient>();
        _serializer.Serialize("data").Returns(new byte[] { 1 });
        var publisher = new AzureEventHubsBrokerPublisher(client, _serializer);

        var ex = new EventHubsException(isTransient: false, "eventhub-name", "Quota exceeded", EventHubsException.FailureReason.MessageSizeExceeded);
        client.SendAsync(Arg.Any<IEnumerable<EventData>>(), Arg.Any<SendEventOptions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(ex);

        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata("corr", "caus", "type"));
        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeFalse();
        result.Error.Should().BeSameAs(ex);
    }

    [Fact]
    public async Task PublishBatchAsync_PublishesAllMessages()
    {
        var client = Substitute.For<EventHubProducerClient>();
        _serializer.Serialize(Arg.Any<string>()).Returns(new byte[] { 1 });
        var publisher = new AzureEventHubsBrokerPublisher(client, _serializer);

        var msg1 = new MessageEnvelope<string>("data1", new OutboxMessageMetadata("c1", "ca1", "type1"));
        var msg2 = new MessageEnvelope<string>("data2", new OutboxMessageMetadata("c2", "ca2", "type2"));

        var results = await publisher.PublishBatchAsync(new[] { msg1, msg2 }, new DispatchContext(CancellationToken.None, 1));

        results.Should().HaveCount(2);
        results[0].Success.Should().BeTrue();
        results[1].Success.Should().BeTrue();

        await client.Received(2).SendAsync(Arg.Any<IEnumerable<EventData>>(), Arg.Any<SendEventOptions>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishRawAsync_Should_Return_DispatchResult_With_CorrelationId_PartitionKey()
    {
        var client = Substitute.For<EventHubProducerClient>();
        var publisher = new AzureEventHubsBrokerPublisher(client, _serializer);

        var payload = Encoding.UTF8.GetBytes("{\"test\":\"data\"}");
        var message = new OutboxMessage(
            Guid.NewGuid(),
            "OrderCreated",
            payload,
            "corr-123",
            "caus-456",
            ReadOnlyMemory<byte>.Empty,
            DateTimeOffset.UtcNow,
            null,
            null,
            OutboxMessageStatus.Pending,
            0,
            null)
        {
            TenantId = "tenant-01"
        };

        var metadata = new OutboxMessageMetadata(
            "corr-123",
            "caus-456",
            "OrderCreated",
            new[] { new MetadataEntry("k", "v") });

        var context = new DispatchContext(CancellationToken.None, 1);

        var result = await publisher.PublishRawAsync(message, metadata, context);
        result.Success.Should().BeTrue();

        await client.Received(1).SendAsync(
            Arg.Is<IEnumerable<EventData>>(events =>
                events != null &&
                events.First().Properties["MessageType"].ToString() == "OrderCreated" &&
                events.First().Properties["CorrelationId"].ToString() == "corr-123" &&
                events.First().Properties["CausationId"].ToString() == "caus-456" &&
                events.First().Properties["TenantId"].ToString() == "tenant-01" &&
                events.First().Properties["k"].ToString() == "v"),
            Arg.Is<SendEventOptions>(opts => opts.PartitionKey == "corr-123"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishRawAsync_WhenCorrelationIdIsNull_UsesTenantIdForPartitionKey()
    {
        var client = Substitute.For<EventHubProducerClient>();
        var publisher = new AzureEventHubsBrokerPublisher(client, _serializer);

        var message = new OutboxMessage(
            Guid.NewGuid(),
            "OrderCreated",
            new byte[] { 1 },
            null,
            null,
            ReadOnlyMemory<byte>.Empty,
            DateTimeOffset.UtcNow,
            null,
            null,
            0,
            0,
            null)
        {
            TenantId = "tenant-99"
        };

        var metadata = new OutboxMessageMetadata(null, null, null);
        var result = await publisher.PublishRawAsync(message, metadata, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeTrue();
        await client.Received(1).SendAsync(
            Arg.Is<IEnumerable<EventData>>(events =>
                events != null &&
                events.First().Properties["MessageType"].ToString() == "OrderCreated" &&
                events.First().Properties["TenantId"].ToString() == "tenant-99" &&
                !events.First().Properties.ContainsKey("CorrelationId") &&
                !events.First().Properties.ContainsKey("CausationId")),
            Arg.Is<SendEventOptions>(opts => opts.PartitionKey == "tenant-99"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishRawAsync_WhenTransientEventHubsException_ReturnsFailAndRetry()
    {
        var client = Substitute.For<EventHubProducerClient>();
        var publisher = new AzureEventHubsBrokerPublisher(client, _serializer);

        var ex = new EventHubsException(isTransient: true, "eventhub-name", "Server busy", EventHubsException.FailureReason.ServiceBusy);
        client.SendAsync(Arg.Any<IEnumerable<EventData>>(), Arg.Any<SendEventOptions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(ex);

        var message = new OutboxMessage(Guid.NewGuid(), "OrderCreated", new byte[] { 1 }, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var metadata = new OutboxMessageMetadata(null, null, null);

        var result = await publisher.PublishRawAsync(message, metadata, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeTrue();
        result.Error.Should().BeSameAs(ex);
    }

    [Fact]
    public async Task PublishRawAsync_WhenGeneralException_ReturnsFailFatal()
    {
        var client = Substitute.For<EventHubProducerClient>();
        var publisher = new AzureEventHubsBrokerPublisher(client, _serializer);

        var ex = new InvalidOperationException("Fatal error");
        client.SendAsync(Arg.Any<IEnumerable<EventData>>(), Arg.Any<SendEventOptions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(ex);

        var message = new OutboxMessage(Guid.NewGuid(), "OrderCreated", new byte[] { 1 }, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var metadata = new OutboxMessageMetadata(null, null, null);

        var result = await publisher.PublishRawAsync(message, metadata, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeFalse();
        result.Error.Should().BeSameAs(ex);
    }
}

