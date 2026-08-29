// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Hosting;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Serialization;
using EricksonLopez.Outbox.Tests.Infrastructure;
using NSubstitute;
using Xunit;

#pragma warning disable CA2012
namespace EricksonLopez.Outbox.Tests.Delivery;

public class ManualOutboxDispatcherTests
{
    [Fact]
    public void Constructor_WhenServiceProviderIsNull_ThrowsArgumentNullException()
    {
        var publisher = Substitute.For<IBrokerPublisher>();
        var resolver = Substitute.For<IOutboxMessageTypeResolver>();

        Action act = () => _ = new ManualOutboxDispatcher(null!, publisher, resolver);
        act.Should().Throw<ArgumentNullException>().WithParameterName("serviceProvider");
    }

    [Fact]
    public void Constructor_WhenPublisherIsNull_ThrowsArgumentNullException()
    {
        var provider = Substitute.For<IServiceProvider>();
        var resolver = Substitute.For<IOutboxMessageTypeResolver>();

        Action act = () => _ = new ManualOutboxDispatcher(provider, null!, resolver);
        act.Should().Throw<ArgumentNullException>().WithParameterName("publisher");
    }

    [Fact]
    public void Constructor_WhenTypeResolverIsNull_ThrowsArgumentNullException()
    {
        var provider = Substitute.For<IServiceProvider>();
        var publisher = Substitute.For<IBrokerPublisher>();

        Action act = () => _ = new ManualOutboxDispatcher(provider, publisher, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("typeResolver");
    }

    [Fact]
    public async Task DispatchPendingAsync_Should_Dispatch_And_Mark()
    {
        var provider = Substitute.For<IServiceProvider>();
        var publisher = Substitute.For<IBrokerPublisher>();
        var resolver = Substitute.For<IOutboxMessageTypeResolver>();
        var repo = Substitute.For<IOutboxRepository>();

        var dispatcher = new ManualOutboxDispatcher(provider, publisher, resolver);

        var message1 = new OutboxMessageTestDataBuilder().WithMessageType("alias").Build();
        _ = repo.FetchPendingAsync(50, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<OutboxMessage>>(new List<OutboxMessage> { message1 }));

        resolver.Resolve("alias").Returns(typeof(string));

        _ = publisher.PublishRawAsync(message1, Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(DispatchResult.Ok()));

        var count = await dispatcher.DispatchPendingAsync(repo);

        count.Should().Be(1);
        await repo.Received().MarkAsDispatchedAsync(Arg.Is<IReadOnlyList<OutboxMessage>>(l => Enumerable.Any(l, m => m.Id == message1.Id)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchPendingAsync_Should_Skip_Unknown_Types()
    {
        var provider = Substitute.For<IServiceProvider>();
        var publisher = Substitute.For<IBrokerPublisher>();
        var resolver = Substitute.For<IOutboxMessageTypeResolver>();
        var repo = Substitute.For<IOutboxRepository>();

        var dispatcher = new ManualOutboxDispatcher(provider, publisher, resolver);

        var message1 = new OutboxMessageTestDataBuilder().WithMessageType("unknown").Build();
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

    [Fact]
    public async Task DispatchPendingAsync_Should_Throw_On_Invalid_Arguments()
    {
        var provider = Substitute.For<IServiceProvider>();
        var publisher = Substitute.For<IBrokerPublisher>();
        var resolver = Substitute.For<IOutboxMessageTypeResolver>();
        var repo = Substitute.For<IOutboxRepository>();

        var dispatcher = new ManualOutboxDispatcher(provider, publisher, resolver);

        var act1 = async () => await dispatcher.DispatchPendingAsync(null!);
        await act1.Should().ThrowAsync<ArgumentNullException>();

        var act2 = async () => await dispatcher.DispatchPendingAsync(repo, batchSize: 0);
        await act2.Should().ThrowAsync<ArgumentOutOfRangeException>();

        var act3 = async () => await dispatcher.DispatchPendingAsync(repo, batchSize: -5);
        await act3.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task DispatchPendingAsync_WhenPublishFails_DoesNotMarkDispatched()
    {
        var provider = Substitute.For<IServiceProvider>();
        var publisher = Substitute.For<IBrokerPublisher>();
        var resolver = Substitute.For<IOutboxMessageTypeResolver>();
        var repo = Substitute.For<IOutboxRepository>();

        var dispatcher = new ManualOutboxDispatcher(provider, publisher, resolver);

        var message1 = new OutboxMessageTestDataBuilder().WithMessageType("alias").Build();
        _ = repo.FetchPendingAsync(50, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<OutboxMessage>>(new List<OutboxMessage> { message1 }));

        resolver.Resolve("alias").Returns(typeof(string));

        _ = publisher.PublishRawAsync(message1, Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(new ValueTask<DispatchResult>(DispatchResult.FailAndRetry(new InvalidOperationException("Transient broker error"))));

        var count = await dispatcher.DispatchPendingAsync(repo);

        count.Should().Be(0);
        await repo.DidNotReceiveWithAnyArgs().MarkAsDispatchedAsync(default!, default);
    }

    [Fact]
    public async Task DispatchPendingAsync_WhenCancelled_BreaksLoopEarly()
    {
        var provider = Substitute.For<IServiceProvider>();
        var publisher = Substitute.For<IBrokerPublisher>();
        var resolver = Substitute.For<IOutboxMessageTypeResolver>();
        resolver.Resolve("alias").Returns(typeof(string));
        var repo = Substitute.For<IOutboxRepository>();

        var dispatcher = new ManualOutboxDispatcher(provider, publisher, resolver);

        var message1 = new OutboxMessageTestDataBuilder().WithMessageType("alias").Build();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        _ = repo.FetchPendingAsync(50, cts.Token)
            .Returns(new ValueTask<IReadOnlyList<OutboxMessage>>(new List<OutboxMessage> { message1 }));

        var count = await dispatcher.DispatchPendingAsync(repo, cancellationToken: cts.Token);

        count.Should().Be(0);
        resolver.DidNotReceiveWithAnyArgs().Resolve(default!);
        await repo.DidNotReceiveWithAnyArgs().MarkAsFailedAsync(default!, default!, default, default);
        await publisher.DidNotReceiveWithAnyArgs().PublishRawAsync(default!, default, default);
    }

    [Fact]
    public async Task DispatchPendingAsync_WhenTypeUnknown_MarksAsFailedDeadLetter()
    {
        var provider = Substitute.For<IServiceProvider>();
        var publisher = Substitute.For<IBrokerPublisher>();
        var resolver = Substitute.For<IOutboxMessageTypeResolver>();
        var repo = Substitute.For<IOutboxRepository>();

        var dispatcher = new ManualOutboxDispatcher(provider, publisher, resolver);

        var message1 = new OutboxMessageTestDataBuilder().WithMessageType("unregistered.type").Build();
        _ = repo.FetchPendingAsync(50, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<OutboxMessage>>(new List<OutboxMessage> { message1 }));

        resolver.Resolve("unregistered.type").Returns((Type?)null);

        var count = await dispatcher.DispatchPendingAsync(repo);

        count.Should().Be(0);
        await repo.Received(1).MarkAsFailedAsync(
            Arg.Is<IReadOnlyList<OutboxMessage>>(l => l.Count == 1 && l[0].Id == message1.Id),
            Arg.Is<string>(s => s.Contains("Unknown message type: unregistered.type")),
            isDeadLetter: true,
            Arg.Any<CancellationToken>());
    }
}







