// Copyright © Erickson Lopez. MIT License.
#pragma warning disable CA2012 // NSubstitute setup doesn't await ValueTasks
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Pipeline;
using EricksonLopez.Result;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Delivery;

public class OutboxPipelineTests
{
    private static OutboxMessage CreateMessage() => new(
        Guid.NewGuid(),
        "order.created",
        new byte[] { 1, 2, 3 },
        "partition-1",
        "topic-1",
        new byte[] { 4, 5 },
        DateTimeOffset.UtcNow,
        null,
        null,
        OutboxMessageStatus.Pending,
        0,
        null);

    [Fact]
    public void Constructor_Throws_WhenMiddlewaresNull()
    {
        OutboxPipelineDelegate terminal = (_, _, _) => ValueTask.FromResult(DispatchResult.Ok());
        var act = () => new OutboxPipeline(null!, terminal);
        act.Should().Throw<ArgumentNullException>().WithParameterName("middlewares");
    }

    [Fact]
    public void Constructor_Throws_WhenTerminalNull()
    {
        var middlewares = new List<IOutboxMiddleware>();
        var act = () => new OutboxPipeline(middlewares, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("terminal");
    }

    [Fact]
    public async Task ExecuteAsync_WithEmptyMiddlewaresArray_ExecutesTerminalDirectly()
    {
        var msg = CreateMessage();
        var meta = new OutboxMessageMetadata();
        bool terminalCalled = false;

        OutboxPipelineDelegate terminal = (m, metadata, ct) =>
        {
            terminalCalled = true;
            m.Should().BeSameAs(msg);
            metadata.Should().Be(meta);
            return ValueTask.FromResult(DispatchResult.Ok());
        };

        var pipeline = new OutboxPipeline(Array.Empty<IOutboxMiddleware>(), terminal);
        var result = await pipeline.ExecuteAsync(msg, meta, CancellationToken.None);

        result.Success.Should().BeTrue();
        terminalCalled.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WithEmptyNonArrayIEnumerable_ExecutesTerminalDirectly()
    {
        var msg = CreateMessage();
        var meta = new OutboxMessageMetadata();
        bool terminalCalled = false;

        OutboxPipelineDelegate terminal = (m, metadata, ct) =>
        {
            terminalCalled = true;
            return ValueTask.FromResult(DispatchResult.Ok());
        };

        var pipeline = new OutboxPipeline(new List<IOutboxMiddleware>(), terminal);
        var result = await pipeline.ExecuteAsync(msg, meta, CancellationToken.None);

        result.Success.Should().BeTrue();
        terminalCalled.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_ExecutesMiddlewaresInOrder_ThenTerminal()
    {
        var msg = CreateMessage();
        var meta = new OutboxMessageMetadata();

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

        var m3 = Substitute.For<IOutboxMiddleware>();
        m3.InvokeAsync(msg, meta, Arg.Any<OutboxPipelineDelegate>(), Arg.Any<CancellationToken>())
            .Returns(x =>
            {
                callOrder.Add("m3");
                var next = x.Arg<OutboxPipelineDelegate>();
                return next(msg, meta, x.Arg<CancellationToken>());
            });

        OutboxPipelineDelegate terminal = (_, _, _) =>
        {
            callOrder.Add("terminal");
            return ValueTask.FromResult(DispatchResult.Ok());
        };

        var pipeline = new OutboxPipeline(new[] { m1, m2, m3 }, terminal);

        var result = await pipeline.ExecuteAsync(msg, meta, CancellationToken.None);

        result.Success.Should().BeTrue();
        callOrder.Should().BeEquivalentTo((string[])["m1", "m2", "m3", "terminal"], options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task ExecuteAsync_WithNonArrayIEnumerable_ExecutesMiddlewaresInOrder()
    {
        var msg = CreateMessage();
        var meta = new OutboxMessageMetadata();

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

        // Pass as List to exercise Enumerable.ToArray
        var list = new List<IOutboxMiddleware> { m1, m2 };
        var pipeline = new OutboxPipeline(list, terminal);

        var result = await pipeline.ExecuteAsync(msg, meta, CancellationToken.None);

        result.Success.Should().BeTrue();
        callOrder.Should().BeEquivalentTo((string[])["m1", "m2", "terminal"], options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task ExecuteAsync_WhenMiddlewareShortCircuits_DoesNotCallNext()
    {
        var msg = CreateMessage();
        var meta = new OutboxMessageMetadata();

        var callOrder = new List<string>();

        var m1 = Substitute.For<IOutboxMiddleware>();
        m1.InvokeAsync(msg, meta, Arg.Any<OutboxPipelineDelegate>(), Arg.Any<CancellationToken>())
            .Returns(x =>
            {
                callOrder.Add("m1");
                return ValueTask.FromResult(DispatchResult.FailFatal("short-circuit"));
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

        result.Success.Should().BeFalse();
        result.Error.Should().BeOfType<OutboxDispatchException>();
        callOrder.Should().BeEquivalentTo((string[])["m1"], options => options.WithStrictOrdering());
        await m2.DidNotReceiveWithAnyArgs().InvokeAsync(default!, default!, default!, default);
    }
}





