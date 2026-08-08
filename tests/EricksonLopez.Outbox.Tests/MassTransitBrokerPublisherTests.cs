using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.MassTransit;
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

    private sealed class ExecutingFakePublishEndpoint : IPublishEndpoint
    {
        public ConnectHandle ConnectPublishObserver(IPublishObserver observer) => throw new NotImplementedException();

        public Task Publish<T>(T message, CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;
        public Task Publish<T>(T message, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default) where T : class
        {
            var ctx = Substitute.For<PublishContext<T>>();
            ctx.Headers.Returns(Substitute.For<SendHeaders>());
            publishPipe.Send(ctx).GetAwaiter().GetResult();
            return Task.CompletedTask;
        }
        public Task Publish<T>(T message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) where T : class 
        {
            var ctx = Substitute.For<PublishContext>();
            ctx.Headers.Returns(Substitute.For<SendHeaders>());
            publishPipe.Send(ctx).GetAwaiter().GetResult();
            return Task.CompletedTask;
        }
        public Task Publish(object message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish(object message, Type messageType, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task Publish(object message, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default)
        {
            var ctx = Substitute.For<PublishContext>();
            ctx.Headers.Returns(Substitute.For<SendHeaders>());
            publishPipe.Send(ctx).GetAwaiter().GetResult();
            return Task.CompletedTask;
        }
        public Task Publish(object message, Type messageType, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default)
        {
            var ctx = Substitute.For<PublishContext>();
            ctx.Headers.Returns(Substitute.For<SendHeaders>());
            publishPipe.Send(ctx).GetAwaiter().GetResult();
            return Task.CompletedTask;
        }
        public Task Publish<T>(object values, CancellationToken cancellationToken = default) where T : class => Task.CompletedTask;
        public Task Publish<T>(object values, IPipe<PublishContext<T>> publishPipe, CancellationToken cancellationToken = default) where T : class
        {
            var ctx = Substitute.For<PublishContext<T>>();
            ctx.Headers.Returns(Substitute.For<SendHeaders>());
            publishPipe.Send(ctx).GetAwaiter().GetResult();
            return Task.CompletedTask;
        }
        public Task Publish<T>(object values, IPipe<PublishContext> publishPipe, CancellationToken cancellationToken = default) where T : class
        {
            var ctx = Substitute.For<PublishContext>();
            ctx.Headers.Returns(Substitute.For<SendHeaders>());
            publishPipe.Send(ctx).GetAwaiter().GetResult();
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task PublishAsync_Should_Succeed()
    {
        var endpoint = new ExecutingFakePublishEndpoint();
        var publisher = new MassTransitBrokerPublisher(endpoint);

        var msg = new MessageEnvelope<string>("data", new MessageMetadata(Guid.NewGuid().ToString(), null, null, new[] { new MetadataEntry("k", "v") }));
        
        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeTrue();
    }
    
    [Fact]
    public async Task PublishAsync_Should_Handle_Invalid_CorrelationId_Guid()
    {
        var endpoint = new ExecutingFakePublishEndpoint();
        var publisher = new MassTransitBrokerPublisher(endpoint);

        var msg = new MessageEnvelope<string>("data", new MessageMetadata("not-a-guid", null, null, new[] { new MetadataEntry("k", "v") }));
        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task PublishAsync_Should_Fail()
    {
        var endpoint = new ThrowingFakePublishEndpoint();
        var publisher = new MassTransitBrokerPublisher(endpoint);

        var msg = new MessageEnvelope<string>("data", new MessageMetadata(null, null, null));
        var result = await publisher.PublishAsync(msg, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeTrue();
    }

    [Fact]
    public async Task PublishBatchAsync_Should_Succeed()
    {
        var endpoint = Substitute.For<IPublishEndpoint>();
        var publisher = new MassTransitBrokerPublisher(endpoint);

        var msg = new MessageEnvelope<string>("data", new MessageMetadata(null, null, null));
        var result = await publisher.PublishBatchAsync(new[] { msg }, new DispatchContext(CancellationToken.None, 1));

        result.Count.Should().Be(1);
        result[0].Success.Should().BeTrue();
    }

    [Fact]
    public async Task PublishRawAsync_Should_Succeed()
    {
        var endpoint = new ExecutingFakePublishEndpoint();
        var publisher = new MassTransitBrokerPublisher(endpoint);

        var msg = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var meta = new MessageMetadata(Guid.NewGuid().ToString(), null, null, new[] { new MetadataEntry("k", "v") });
        
        var result = await publisher.PublishRawAsync(msg, meta, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeTrue();
    }
    
    [Fact]
    public async Task PublishRawAsync_Should_Handle_Invalid_CorrelationId_Guid()
    {
        var endpoint = new ExecutingFakePublishEndpoint();
        var publisher = new MassTransitBrokerPublisher(endpoint);

        var msg = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var meta = new MessageMetadata("not-a-guid", null, null, new[] { new MetadataEntry("k", "v") });
        var result = await publisher.PublishRawAsync(msg, meta, new DispatchContext(CancellationToken.None, 1));

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task PublishRawAsync_Should_Fail()
    {
        var endpoint = new ThrowingFakePublishEndpoint();
        var publisher = new MassTransitBrokerPublisher(endpoint);

        var msg = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var meta = new MessageMetadata(null, null, null);
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


