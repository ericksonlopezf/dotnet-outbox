#pragma warning disable CA2012 // NSubstitute setup doesn't await ValueTasks
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;

using EricksonLopez.Outbox.Pipeline;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Pipeline;

public class OutboxPipelineTests
{
    [Fact]
    public void Constructor_Throws_WhenMiddlewaresNull()
    {
        OutboxPipelineDelegate terminal = (_, _, _) => ValueTask.FromResult(DispatchResult.Ok());
        Assert.Throws<ArgumentNullException>(() => new OutboxPipeline(null!, terminal));
    }

    [Fact]
    public void Constructor_Throws_WhenTerminalNull()
    {
        var middlewares = new List<IOutboxMiddleware>();
        Assert.Throws<ArgumentNullException>(() => new OutboxPipeline(middlewares, null!));
    }

    [Fact]
    public async Task ExecuteAsync_ExecutesMiddlewaresInOrder_ThenTerminal()
    {
        var msg = new OutboxMessage(Guid.NewGuid(), "test", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
        var meta = new MessageMetadata();

        var callOrder = new List<string>();

        var m1 = Substitute.For<IOutboxMiddleware>();
        m1.InvokeAsync(msg, meta, Arg.Any<OutboxPipelineDelegate>(), Arg.Any<CancellationToken>())
            .Returns(x =>
            {
                callOrder.Add("m1");
                var next = x.Arg<OutboxPipelineDelegate>();
                return next(msg, meta, x.Arg<CancellationToken>());
            });

        var m2 = Substitute.For<IOutboxMiddleware>();
        m2.InvokeAsync(msg, meta, Arg.Any<OutboxPipelineDelegate>(), Arg.Any<CancellationToken>())
            .Returns(x =>
            {
                callOrder.Add("m2");
                var next = x.Arg<OutboxPipelineDelegate>();
                return next(msg, meta, x.Arg<CancellationToken>());
            });

        OutboxPipelineDelegate terminal = (_, _, _) =>
        {
            callOrder.Add("terminal");
            return ValueTask.FromResult(DispatchResult.Ok());
        };

        var pipeline = new OutboxPipeline(new[] { m1, m2 }, terminal);

        var result = await pipeline.ExecuteAsync(msg, meta, CancellationToken.None);

        result.Success.Should().BeTrue();
        callOrder.Should().BeEquivalentTo((string[])["m1", "m2", "terminal"], options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task ExecuteAsync_WhenMiddlewareShortCircuits_DoesNotCallNext()
    {
        var msg = new OutboxMessage(Guid.NewGuid(), "test", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
        var meta = new MessageMetadata();

        var callOrder = new List<string>();

        var m1 = Substitute.For<IOutboxMiddleware>();
        m1.InvokeAsync(msg, meta, Arg.Any<OutboxPipelineDelegate>(), Arg.Any<CancellationToken>())
            .Returns(x =>
            {
                callOrder.Add("m1");
                return ValueTask.FromResult(DispatchResult.FailFatal("short-circuit"));
            });

        OutboxPipelineDelegate terminal = (_, _, _) =>
        {
            callOrder.Add("terminal");
            return ValueTask.FromResult(DispatchResult.Ok());
        };

        var pipeline = new OutboxPipeline(new[] { m1 }, terminal);

        var result = await pipeline.ExecuteAsync(msg, meta, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Error.Should().BeOfType<OutboxDispatchException>();
        callOrder.Should().BeEquivalentTo((string[])["m1"], options => options.WithStrictOrdering());
    }

    [Fact]
    public void Constructor_WithNonArrayIEnumerable_CreatesPipelineSuccessfully()
    {
        var terminal = new OutboxPipelineDelegate((_, _, _) => ValueTask.FromResult(DispatchResult.Ok()));
        var middlewares = new List<IOutboxMiddleware> { Substitute.For<IOutboxMiddleware>() };
        
        var pipeline = new OutboxPipeline(middlewares, terminal);
        
        pipeline.Should().NotBeNull();
    }
    [Fact]
    public void Constructor_ShouldThrow_WhenMiddlewaresIsNull()
    {
        var act = () => new OutboxPipeline(null!, (m, meta, ct) => ValueTask.FromResult(default(DispatchResult)));
        act.Should().Throw<ArgumentNullException>();
    }
    
    [Fact]
    public void Constructor_ShouldThrow_WhenTerminalIsNull()
    {
        var act = () => new OutboxPipeline(Array.Empty<IOutboxMiddleware>(), null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
