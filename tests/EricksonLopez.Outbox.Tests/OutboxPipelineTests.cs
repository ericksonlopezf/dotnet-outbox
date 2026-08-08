using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Pipeline;
using Xunit;

namespace EricksonLopez.Outbox.Tests;

public class OutboxPipelineTests
{
    private sealed class TestMiddleware : IOutboxMiddleware
    {
        private readonly Action _onInvoke;

        public TestMiddleware(Action onInvoke)
        {
            _onInvoke = onInvoke;
        }

        public async ValueTask<DispatchResult> InvokeAsync(
            OutboxMessage message,
            MessageMetadata metadata,
            OutboxPipelineDelegate next,
            CancellationToken cancellationToken)
        {
            _onInvoke();
            return await next(message, metadata, cancellationToken);
        }
    }

    [Fact]
    public async Task ExecuteAsync_Should_Call_Middleware_And_Delegate()
    {
        bool middleware1Called = false;
        bool middleware2Called = false;

        var middlewares = new[] 
        { 
            new TestMiddleware(() => {
                middleware1Called = true;
                middleware2Called.Should().BeFalse("Middleware 1 must be called first");
            }),
            new TestMiddleware(() => {
                middleware1Called.Should().BeTrue("Middleware 2 must be called after 1");
                middleware2Called = true;
            })
        };

        var pipeline = new OutboxPipeline(middlewares, (m, meta, c) => new ValueTask<DispatchResult>(DispatchResult.Ok()));

        var msg = new OutboxMessage(Guid.NewGuid(), "alias", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var metadata = new MessageMetadata(Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "TestMessage", null);
        var result = await pipeline.ExecuteAsync(msg, metadata, CancellationToken.None);

        result.Success.Should().BeTrue();
        middleware1Called.Should().BeTrue();
        middleware2Called.Should().BeTrue();
    }
}


