using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Google.Cloud.PubSub.V1;
using AwesomeAssertions;
using EricksonLopez.Outbox.Brokers.GooglePubSub;
using EricksonLopez.Outbox;
using Google.Api.Gax.Grpc;
using Grpc.Core;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Brokers;

public class GooglePubSubBrokerPublisherTests
{
    [Fact]
    public async Task PublishAsync_Should_Throw_NotSupportedException()
    {
        var client = Substitute.For<PublisherServiceApiClient>();
        var publisher = new GooglePubSubBrokerPublisher(client, "project-id");

        var msg = new MessageEnvelope<string>("data", new MessageMetadata(null, null, null));
        var act = () => publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));

        await Assert.ThrowsAsync<NotSupportedException>(async () => await act());
    }

    [Fact]
    public async Task PublishBatchAsync_Should_Throw_NotSupportedException()
    {
        var client = Substitute.For<PublisherServiceApiClient>();
        var publisher = new GooglePubSubBrokerPublisher(client, "project-id");

        var msg = new MessageEnvelope<string>("data", new MessageMetadata(null, null, null));
        var act = () => publisher.PublishBatchAsync(new[] { msg }, new DispatchContext(CancellationToken.None, 1));

        await Assert.ThrowsAsync<NotSupportedException>(async () => await act());
    }

    [Fact]
    public async Task PublishBatchAsync_WithEmptyList_Should_Throw_NotSupportedException()
    {
        var client = Substitute.For<PublisherServiceApiClient>();
        var publisher = new GooglePubSubBrokerPublisher(client, "project-id");

        var act = () => publisher.PublishBatchAsync(Array.Empty<MessageEnvelope<string>>(), new DispatchContext(CancellationToken.None, 1));
        
        await Assert.ThrowsAsync<NotSupportedException>(async () => await act());
    }

    [Fact]
    public async Task PublishRawAsync_Should_Succeed()
    {
        var client = Substitute.For<PublisherServiceApiClient>();
        var publisher = new GooglePubSubBrokerPublisher(client, "project-id");

        var payload = System.Text.Encoding.UTF8.GetBytes("test_payload");
        var msg = new OutboxMessage(Guid.NewGuid(), "alias", payload, null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var meta = new MessageMetadata("corr", "caus", "type", new[] { new MetadataEntry("k", "v") });
        var result = await publisher.PublishRawAsync(msg, meta, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeTrue();
        
        await client.Received(1).PublishAsync(
            Arg.Is<TopicName>(t => t.TopicId == "alias" && t.ProjectId == "project-id"),
            Arg.Is<IEnumerable<PubsubMessage>>(msgs => 
                msgs.FirstOrDefault() != null &&
                msgs.First().Data.ToStringUtf8() == "test_payload" &&
                msgs.First().Attributes["message_type"] == "alias" &&
                msgs.First().Attributes["correlation_id"] == "corr" &&
                msgs.First().Attributes["causation_id"] == "caus" &&
                msgs.First().Attributes["k"] == "v"),
            Arg.Any<CallSettings>());
    }

    [Fact]
    public async Task PublishRawAsync_Should_Use_Custom_TopicNamingStrategy()
    {
        var client = Substitute.For<PublisherServiceApiClient>();
        client.PublishAsync(Arg.Any<TopicName>(), Arg.Any<IEnumerable<PubsubMessage>>(), Arg.Any<CallSettings>()).Returns(new PublishResponse());
        
        // Pass custom strategy
        var publisher = new GooglePubSubBrokerPublisher(client, "project-id", alias => $"custom-{alias}");

        var payload = System.Text.Encoding.UTF8.GetBytes("{\"data\":\"val\"}");
        var msg = new OutboxMessage(Guid.NewGuid(), "alias", payload, null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var meta = new MessageMetadata(null, null, null);
        var result = await publisher.PublishRawAsync(msg, meta, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeTrue();
        
        await client.Received(1).PublishAsync(
            Arg.Is<TopicName>(t => t.TopicId == "custom-alias" && t.ProjectId == "project-id"),
            Arg.Is<IEnumerable<PubsubMessage>>(msgs => 
                msgs.FirstOrDefault() != null &&
                msgs.First().Data.ToStringUtf8() == "{\"data\":\"val\"}"),
            Arg.Any<CallSettings>());
    }

    [Fact]
    public async Task PublishRawAsync_Should_Fail()
    {
        var client = Substitute.For<PublisherServiceApiClient>();
        client.PublishAsync(Arg.Any<TopicName>(), Arg.Any<IEnumerable<PubsubMessage>>(), Arg.Any<CallSettings>()).ThrowsAsync(new InvalidOperationException("test"));
        var publisher = new GooglePubSubBrokerPublisher(client, "project-id");

        var msg = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var meta = new MessageMetadata(null, null, null);
        var result = await publisher.PublishRawAsync(msg, meta, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeTrue();
    }

    [Fact]
    public void Constructor_Should_Throw_On_Empty_ProjectId()
    {
        var client = Substitute.For<PublisherServiceApiClient>();
        Action act1 = () => _ = new GooglePubSubBrokerPublisher(client, null!);
        Action act2 = () => _ = new GooglePubSubBrokerPublisher(client, "");
        Action act3 = () => _ = new GooglePubSubBrokerPublisher(client, "   ");

        act1.Should().Throw<ArgumentException>();
        act2.Should().Throw<ArgumentException>();
        act3.Should().Throw<ArgumentException>();
    }
}



