// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.MassTransit;
using EricksonLopez.Result;
using MassTransit;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Tests.MassTransit;

public class MassTransitBrokerPublisherTests
{
    private sealed class ThrowingFakePublishEndpoint : IPublishEndpoint
    {
        public ConnectHandle ConnectPublishObserver(IPublishObserver observer) => throw new NotImplementedException();
        public Task Publish<T>(T message, CancellationToken cancellationToken = default) where T : class => throw new InvalidOperationException("test");
        public Task Publish<T>(T message, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default) where T : class => throw new InvalidOperationException("test");
        public Task Publish<T>(T message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) where T : class => throw new InvalidOperationException("test");
        public Task Publish(object message, CancellationToken cancellationToken = default) => throw new InvalidOperationException("test");
        public Task Publish(object message, Type messageType, CancellationToken cancellationToken = default) => throw new InvalidOperationException("test");
        public Task Publish(object message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) => throw new InvalidOperationException("test");
        public Task Publish(object message, Type messageType, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) => throw new InvalidOperationException("test");
        public Task Publish<T>(object values, CancellationToken cancellationToken = default) where T : class => throw new InvalidOperationException("test");
        public Task Publish<T>(object values, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default) where T : class => throw new InvalidOperationException("test");
        public Task Publish<T>(object values, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) where T : class => throw new InvalidOperationException("test");
    }

    private sealed class CapturingPublishEndpoint : IPublishEndpoint
    {
        public object? PublishedMessage { get; private set; }
        public PublishContext? LastPublishContext { get; private set; }
        public SendHeaders LastHeaders { get; } = Substitute.For<SendHeaders>();

        public ConnectHandle ConnectPublishObserver(IPublishObserver observer) => throw new NotImplementedException();

        public Task Publish<T>(T message, CancellationToken cancellationToken = default) where T : class
        {
            PublishedMessage = message;
            return Task.CompletedTask;
        }

        public Task Publish<T>(T message, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default) where T : class
        {
            PublishedMessage = message;
            var ctx = Substitute.For<PublishContext<T>>();
            ctx.Headers.Returns(LastHeaders);
            LastPublishContext = ctx;
            publishPipe.Send(ctx).GetAwaiter().GetResult();
            return Task.CompletedTask;
        }

        public Task Publish<T>(T message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) where T : class 
        {
            PublishedMessage = message;
            var ctx = Substitute.For<PublishContext>();
            ctx.Headers.Returns(LastHeaders);
            LastPublishContext = ctx;
            publishPipe.Send(ctx).GetAwaiter().GetResult();
            return Task.CompletedTask;
        }

        public Task Publish(object message, CancellationToken cancellationToken = default)
        {
            PublishedMessage = message;
            return Task.CompletedTask;
        }

        public Task Publish(object message, Type messageType, CancellationToken cancellationToken = default)
        {
            PublishedMessage = message;
            return Task.CompletedTask;
        }

        public Task Publish(object message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default)
        {
            PublishedMessage = message;
            var ctx = Substitute.For<PublishContext>();
            ctx.Headers.Returns(LastHeaders);
            LastPublishContext = ctx;
            publishPipe.Send(ctx).GetAwaiter().GetResult();
            return Task.CompletedTask;
        }

        public Task Publish(object message, Type messageType, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default)
        {
            PublishedMessage = message;
            var ctx = Substitute.For<PublishContext>();
            ctx.Headers.Returns(LastHeaders);
            LastPublishContext = ctx;
            publishPipe.Send(ctx).GetAwaiter().GetResult();
            return Task.CompletedTask;
        }

        public Task Publish<T>(object values, CancellationToken cancellationToken = default) where T : class
        {
            PublishedMessage = values;
            return Task.CompletedTask;
        }

        public Task Publish<T>(object values, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default) where T : class
        {
            PublishedMessage = values;
            var ctx = Substitute.For<PublishContext<T>>();
            ctx.Headers.Returns(LastHeaders);
            LastPublishContext = ctx;
            publishPipe.Send(ctx).GetAwaiter().GetResult();
            return Task.CompletedTask;
        }

        public Task Publish<T>(object values, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) where T : class
        {
            PublishedMessage = values;
            var ctx = Substitute.For<PublishContext>();
            ctx.Headers.Returns(LastHeaders);
            LastPublishContext = ctx;
            publishPipe.Send(ctx).GetAwaiter().GetResult();
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task PublishAsync_Should_Succeed()
    {
        var endpoint = new CapturingPublishEndpoint();
        var publisher = new MassTransitBrokerPublisher(endpoint);

        var corrId = Guid.NewGuid();
        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata(corrId.ToString(), null, null, new[] { new MetadataEntry("k", "v") }));
        
        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeTrue();
        endpoint.PublishedMessage.Should().Be("data");
        endpoint.LastPublishContext!.Received(1).CorrelationId = corrId;
        endpoint.LastHeaders.Received(1).Set("k", "v");
    }
    
    [Fact]
    public async Task PublishAsync_Should_Handle_Invalid_CorrelationId_Guid()
    {
        var endpoint = new CapturingPublishEndpoint();
        var publisher = new MassTransitBrokerPublisher(endpoint);

        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata("not-a-guid", null, null, new[] { new MetadataEntry("k", "v") }));
        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeTrue();
        endpoint.LastPublishContext!.DidNotReceive().CorrelationId = Arg.Any<Guid?>();
        endpoint.LastHeaders.Received(1).Set("k", "v");
    }

    [Fact]
    public async Task PublishAsync_Should_Fail()
    {
        var endpoint = new ThrowingFakePublishEndpoint();
        var publisher = new MassTransitBrokerPublisher(endpoint);

        var msg = new MessageEnvelope<string>("data", new OutboxMessageMetadata(null, null, null));
        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeTrue();
    }

    [Fact]
    public async Task PublishBatchAsync_Should_Succeed()
    {
        var endpoint = new CapturingPublishEndpoint();
        var publisher = new MassTransitBrokerPublisher(endpoint);

        var msg1 = new MessageEnvelope<string>("data1", new OutboxMessageMetadata(null, null, null));
        var msg2 = new MessageEnvelope<string>("data2", new OutboxMessageMetadata(null, null, null));
        var result = await publisher.PublishBatchAsync(new[] { msg1, msg2 }, new DispatchContext(CancellationToken.None, 1));

        result.Count.Should().Be(2);
        result[0].Success.Should().BeTrue();
        result[1].Success.Should().BeTrue();
    }

    [Fact]
    public async Task PublishRawAsync_Should_Succeed()
    {
        var endpoint = new CapturingPublishEndpoint();
        var publisher = new MassTransitBrokerPublisher(endpoint);

        var corrId = Guid.NewGuid();
        var msg = new OutboxMessage(Guid.NewGuid(), "OrderCreated", System.Text.Encoding.UTF8.GetBytes("{\"id\":1}"), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var meta = new OutboxMessageMetadata(corrId.ToString(), null, null, new[] { new MetadataEntry("k", "v") });
        
        var result = await publisher.PublishRawAsync(msg, meta, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeTrue();
        var env = endpoint.PublishedMessage.Should().BeOfType<Dictionary<string, object?>>().Subject;
        env["payload"].Should().Be("{\"id\":1}");
        env["messageType"].Should().Be("OrderCreated");
        endpoint.LastPublishContext!.Received(1).CorrelationId = corrId;
        endpoint.LastHeaders.Received(1).Set("outbox.message_type", "OrderCreated");
        endpoint.LastHeaders.Received(1).Set("k", "v");
    }
    
    [Fact]
    public async Task PublishRawAsync_Should_Handle_Invalid_CorrelationId_Guid()
    {
        var endpoint = new CapturingPublishEndpoint();
        var publisher = new MassTransitBrokerPublisher(endpoint);

        var msg = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var meta = new OutboxMessageMetadata("not-a-guid", null, null, new[] { new MetadataEntry("k", "v") });
        var result = await publisher.PublishRawAsync(msg, meta, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeTrue();
        endpoint.LastPublishContext!.DidNotReceive().CorrelationId = Arg.Any<Guid?>();
        endpoint.LastHeaders.Received(1).Set("outbox.message_type", "alias");
        endpoint.LastHeaders.Received(1).Set("k", "v");
    }

    [Fact]
    public async Task PublishRawAsync_Should_Fail()
    {
        var endpoint = new ThrowingFakePublishEndpoint();
        var publisher = new MassTransitBrokerPublisher(endpoint);

        var msg = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var meta = new OutboxMessageMetadata(null, null, null);
        var result = await publisher.PublishRawAsync(msg, meta, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeTrue();
    }

    [Fact]
    public void Constructor_NullPublishEndpoint_ThrowsArgumentNullException()
    {
        Action act = () => _ = new MassTransitBrokerPublisher(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("publishEndpoint");
    }
}

