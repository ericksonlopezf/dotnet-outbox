#pragma warning disable CA2012
using System;
using System.Collections.Generic;

using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Hosting;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Serialization;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Tests;

public class ManualOutboxDispatcherTests
{
    [Fact]
    public void Constructor_Should_Throw_On_Null_Args()
    {
        var provider = Substitute.For<IServiceProvider>();
        var publisher = Substitute.For<IBrokerPublisher>();
        var resolver = Substitute.For<IOutboxMessageTypeResolver>();

        Action act1 = () => _ = new ManualOutboxDispatcher(null!, publisher, resolver);
        act1.Should().Throw<ArgumentNullException>();

        Action act2 = () => _ = new ManualOutboxDispatcher(provider, null!, resolver);
        act2.Should().Throw<ArgumentNullException>();

        Action act3 = () => _ = new ManualOutboxDispatcher(provider, publisher, null!);
        act3.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public async Task DispatchPendingAsync_Should_Dispatch_And_Mark()
    {
        var provider = Substitute.For<IServiceProvider>();
        var publisher = Substitute.For<IBrokerPublisher>();
        var resolver = Substitute.For<IOutboxMessageTypeResolver>();
        var repo = Substitute.For<IOutboxRepository>();

        var dispatcher = new ManualOutboxDispatcher(provider, publisher, resolver);

        var message1 = new OutboxMessage(Guid.NewGuid(), "alias", default, null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        _ = repo.FetchPendingAsync(50, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<OutboxMessage>>(new List<OutboxMessage> { message1 }));

        resolver.Resolve("alias").Returns(typeof(string));

        _ = publisher.PublishRawAsync(message1, Arg.Any<MessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(DispatchResult.Ok()));

        var count = await dispatcher.DispatchPendingAsync(repo);

        count.Should().Be(1);
        await repo.Received().MarkAsDispatchedAsync(Arg.Is<IReadOnlyList<OutboxMessage>>(l => System.Linq.Enumerable.Any(l, m => m.Id == message1.Id)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchPendingAsync_Should_Skip_Unknown_Types()
    {
        var provider = Substitute.For<IServiceProvider>();
        var publisher = Substitute.For<IBrokerPublisher>();
        var resolver = Substitute.For<IOutboxMessageTypeResolver>();
        var repo = Substitute.For<IOutboxRepository>();

        var dispatcher = new ManualOutboxDispatcher(provider, publisher, resolver);

        var message1 = new OutboxMessage(Guid.NewGuid(), "unknown", default, null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        _ = repo.FetchPendingAsync(50, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<OutboxMessage>>(new List<OutboxMessage> { message1 }));

        resolver.Resolve("unknown").Returns((Type?)null);

        var count = await dispatcher.DispatchPendingAsync(repo);

        count.Should().Be(0);
        await publisher.DidNotReceiveWithAnyArgs().PublishRawAsync(default!, default, default);
    }

    [Fact]
    public async Task DispatchPendingAsync_Should_Return_Zero_If_No_Messages()
    {
        var provider = Substitute.For<IServiceProvider>();
        var publisher = Substitute.For<IBrokerPublisher>();
        var resolver = Substitute.For<IOutboxMessageTypeResolver>();
        var repo = Substitute.For<IOutboxRepository>();

        var dispatcher = new ManualOutboxDispatcher(provider, publisher, resolver);

        _ = repo.FetchPendingAsync(50, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<OutboxMessage>>(new List<OutboxMessage>()));

        var count = await dispatcher.DispatchPendingAsync(repo);

        count.Should().Be(0);
    }
}


