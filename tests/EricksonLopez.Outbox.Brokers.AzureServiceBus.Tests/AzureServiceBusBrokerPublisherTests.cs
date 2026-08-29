// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Azure.Messaging.ServiceBus;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Brokers.AzureServiceBus;
using EricksonLopez.Outbox.Serialization;
using EricksonLopez.Result;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Brokers;

public class AzureServiceBusBrokerPublisherTests
{
    [Fact]
    public void Constructor_NullGuards()
    {
        var sender = Substitute.For<ServiceBusSender>();
        var serializer = Substitute.For<IOutboxSerializer>();

        Action act1 = () => { _ = new AzureServiceBusBrokerPublisher(null!, serializer); };
        act1.Should().Throw<ArgumentNullException>().WithParameterName("sender");

        Action act2 = () => { _ = new AzureServiceBusBrokerPublisher(sender, null!); };
        act2.Should().Throw<ArgumentNullException>().WithParameterName("serializer");
    }

    [Fact]
    public async Task PublishBatchAsync_WhenBatchIsFull_ReturnsFailFatal()
    {
        var sender = Substitute.For<ServiceBusSender>();
        var serializer = Substitute.For<IOutboxSerializer>();
        var publisher = new AzureServiceBusBrokerPublisher(sender, serializer);

        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata("corr", "caus", "type1"));
        var batch = ServiceBusModelFactory.ServiceBusMessageBatch(
            batchSizeBytes: 1024,
            batchMessageStore: new List<ServiceBusMessage>(),
            tryAddCallback: _ => false);
        sender.CreateMessageBatchAsync(Arg.Any<CancellationToken>()).Returns(batch);

        var result = await publisher.PublishBatchAsync(new[] { msg }, new DispatchContext(CancellationToken.None, 1));

        result.Should().HaveCount(1);
        result[0].Success.Should().BeFalse();
        result[0].ShouldRetry.Should().BeFalse();
        result[0].Error.Should().BeOfType<InvalidOperationException>().Which.Message.Should().Contain("Message batch is too large");
    }

    [Fact]
    public async Task PublishRawAsync_WhenNonTransientException_ReturnsFailAndRetry()
    {
        var sender = Substitute.For<ServiceBusSender>();
        var serializer = Substitute.For<IOutboxSerializer>();
        var publisher = new AzureServiceBusBrokerPublisher(sender, serializer);

        var msg = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var meta = new OutboxMessageMetadata(null, null, null);
        sender.SendMessageAsync(Arg.Any<ServiceBusMessage>(), Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("General error"));

        var result = await publisher.PublishRawAsync(msg, meta, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeTrue();
    }

    [Fact]
    public async Task PublishAsync_Should_Succeed()
    {
        var sender = Substitute.For<ServiceBusSender>();
        var serializer = Substitute.For<IOutboxSerializer>();
        var publisher = new AzureServiceBusBrokerPublisher(sender, serializer);

        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata("corr", "caus", "type1", new[] { new MetadataEntry("k", "v") }));
        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeTrue();
        await sender.Received(1).SendMessageAsync(Arg.Is<ServiceBusMessage>(m => 
            m.Subject == "type1" &&
            m.CorrelationId == "corr" &&
            m.ApplicationProperties.ContainsKey("MessageType") && m.ApplicationProperties["MessageType"].ToString() == "type1" &&
            m.ApplicationProperties.ContainsKey("k") && m.ApplicationProperties["k"].ToString() == "v"
        ), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishAsync_Should_Fail_And_Retry_On_Transient()
    {
        var sender = Substitute.For<ServiceBusSender>();
        var serializer = Substitute.For<IOutboxSerializer>();
        var publisher = new AzureServiceBusBrokerPublisher(sender, serializer);

        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata(null, null, null));
        sender.SendMessageAsync(Arg.Any<ServiceBusMessage>(), Arg.Any<CancellationToken>()).ThrowsAsync(new ServiceBusException("transient", ServiceBusFailureReason.ServiceBusy));

        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeTrue();
    }

    [Fact]
    public async Task PublishAsync_Should_Fail_Fatal_On_NonTransient()
    {
        var sender = Substitute.For<ServiceBusSender>();
        var serializer = Substitute.For<IOutboxSerializer>();
        var publisher = new AzureServiceBusBrokerPublisher(sender, serializer);

        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata(null, null, null));
        sender.SendMessageAsync(Arg.Any<ServiceBusMessage>(), Arg.Any<CancellationToken>()).ThrowsAsync(new ServiceBusException("fatal", ServiceBusFailureReason.MessageSizeExceeded));

        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeFalse();
        result.Error.Should().BeOfType<ServiceBusException>().Which.Reason.Should().Be(ServiceBusFailureReason.MessageSizeExceeded);
    }

    [Fact]
    public async Task PublishBatchAsync_Should_Succeed()
    {
        var sender = Substitute.For<ServiceBusSender>();
        var serializer = Substitute.For<IOutboxSerializer>();
        var publisher = new AzureServiceBusBrokerPublisher(sender, serializer);

        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata("corr", "caus", "type1", new[] { new MetadataEntry("k", "v") }));
        var backingList = new List<ServiceBusMessage>();
        var batch = ServiceBusModelFactory.ServiceBusMessageBatch(1024, backingList);
        sender.CreateMessageBatchAsync(Arg.Any<CancellationToken>()).Returns(batch);

        var result = await publisher.PublishBatchAsync(new[] { msg }, new DispatchContext(CancellationToken.None, 1));

        result.Count.Should().Be(1);
        result[0].Success.Should().BeTrue();
        
        backingList.Should().HaveCount(1);
        backingList[0].CorrelationId.Should().Be("corr");
        backingList[0].ApplicationProperties["CausationId"].Should().Be("caus");
        backingList[0].ApplicationProperties["MessageType"].Should().Be("type1");
        backingList[0].ApplicationProperties["k"].Should().Be("v");
        
        await sender.Received(1).SendMessagesAsync(batch, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishBatchAsync_Should_Fail_And_Retry_On_Transient()
    {
        var sender = Substitute.For<ServiceBusSender>();
        var serializer = Substitute.For<IOutboxSerializer>();
        var publisher = new AzureServiceBusBrokerPublisher(sender, serializer);

        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata(null, null, null));
        var batch = ServiceBusModelFactory.ServiceBusMessageBatch(1024, new List<ServiceBusMessage>());
        sender.CreateMessageBatchAsync(Arg.Any<CancellationToken>()).Returns(batch);

        sender.SendMessagesAsync(Arg.Any<ServiceBusMessageBatch>(), Arg.Any<CancellationToken>()).ThrowsAsync(new ServiceBusException("transient", ServiceBusFailureReason.ServiceBusy));

        var result = await publisher.PublishBatchAsync(new[] { msg }, new DispatchContext(CancellationToken.None, 1));

        result.Count.Should().Be(1);
        result[0].Success.Should().BeFalse();
        result[0].ShouldRetry.Should().BeTrue();
    }

    [Fact]
    public async Task PublishBatchAsync_Should_Fail_Fatal_On_NonTransient()
    {
        var sender = Substitute.For<ServiceBusSender>();
        var serializer = Substitute.For<IOutboxSerializer>();
        var publisher = new AzureServiceBusBrokerPublisher(sender, serializer);

        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata(null, null, null));
        var batch = ServiceBusModelFactory.ServiceBusMessageBatch(1024, new List<ServiceBusMessage>());
        sender.CreateMessageBatchAsync(Arg.Any<CancellationToken>()).Returns(batch);

        sender.SendMessagesAsync(Arg.Any<ServiceBusMessageBatch>(), Arg.Any<CancellationToken>()).ThrowsAsync(new ServiceBusException("fatal", ServiceBusFailureReason.MessageSizeExceeded));

        var result = await publisher.PublishBatchAsync(new[] { msg }, new DispatchContext(CancellationToken.None, 1));

        result.Count.Should().Be(1);
        result[0].Success.Should().BeFalse();
        result[0].ShouldRetry.Should().BeFalse();
    }

    [Fact]
    public async Task PublishRawAsync_Should_Succeed()
    {
        var sender = Substitute.For<ServiceBusSender>();
        var serializer = Substitute.For<IOutboxSerializer>();
        var publisher = new AzureServiceBusBrokerPublisher(sender, serializer);

        var msg = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var meta = new OutboxMessageMetadata("corr", "caus", "type1", new[] { new MetadataEntry("k", "v") });
        var result = await publisher.PublishRawAsync(msg, meta, new DispatchContext(CancellationToken.None, 1));
        
        result.Success.Should().BeTrue();
        await sender.Received(1).SendMessageAsync(Arg.Is<ServiceBusMessage>(m => 
            m.CorrelationId == "corr" &&
            (string)m.ApplicationProperties["MessageType"] == "alias" &&
            (string)m.ApplicationProperties["CausationId"] == "caus" &&
            (string)m.ApplicationProperties["k"] == "v"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishRawAsync_Should_Fail_And_Retry_On_Transient()
    {
        var sender = Substitute.For<ServiceBusSender>();
        var serializer = Substitute.For<IOutboxSerializer>();
        var publisher = new AzureServiceBusBrokerPublisher(sender, serializer);

        var msg = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var meta = new OutboxMessageMetadata(null, null, null);
        sender.SendMessageAsync(Arg.Any<ServiceBusMessage>(), Arg.Any<CancellationToken>()).ThrowsAsync(new ServiceBusException("transient", ServiceBusFailureReason.ServiceBusy));

        var result = await publisher.PublishRawAsync(msg, meta, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeTrue();
    }
}








