// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Testing;
using EricksonLopez.Outbox.Tests.Infrastructure;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Testing;

public class FakeOutboxDispatcherTests
{
    private static OutboxMessage CreateMessage() =>
        new OutboxMessageTestDataBuilder().WithMessageType("a").WithPayload(Array.Empty<byte>()).Build();

    [Fact]
    public async Task DispatchAsync_WithMessages_Should_Dispatch()
    {
        var broker = new FakeBrokerPublisher();
        var dispatcher = new FakeOutboxDispatcher(broker);
        
        var msgs = new[] { CreateMessage() };
        
        var count = await dispatcher.DispatchAsync(msgs);
        
        count.Should().Be(1);
        dispatcher.DispatchedMessages.Count.Should().Be(1);
    }

    [Fact]
    public async Task DispatchAsync_WithRepository_Should_Fetch_And_Dispatch()
    {
        var repo = Substitute.For<IOutboxRepository>();
        var msgs = new[] { CreateMessage() };
        repo.FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(msgs);
        
        var broker = new FakeBrokerPublisher();
        var dispatcher = new FakeOutboxDispatcher(broker, repo);
        
        var count = await dispatcher.DispatchAsync();
        
        count.Should().Be(1);
        await repo.Received(1).MarkAsDispatchedAsync(
            Arg.Is<IReadOnlyList<OutboxMessage>>(b => b.Count == 1 && b[0].Id == msgs[0].Id),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchAsync_Should_Skip_If_Canceled()
    {
        var broker = new FakeBrokerPublisher();
        var dispatcher = new FakeOutboxDispatcher(broker);
        
        var msgs = new[] { CreateMessage() };
        
        var cts = new CancellationTokenSource();
        cts.Cancel();
        
        var count = await dispatcher.DispatchAsync(msgs, cts.Token);
        
        count.Should().Be(0);
        dispatcher.DispatchedMessages.Count.Should().Be(0);
    }

    [Fact]
    public async Task DispatchAsync_Should_Not_Add_If_Broker_Fails()
    {
        var broker = new FakeBrokerPublisher().WithFailure();
        var dispatcher = new FakeOutboxDispatcher(broker);
        
        var msgs = new[] { CreateMessage() };
        
        var count = await dispatcher.DispatchAsync(msgs);
        
        count.Should().Be(0);
        dispatcher.DispatchedMessages.Count.Should().Be(0);
    }

    [Fact]
    public async Task DispatchAsync_WithNoMessages_Should_Return_Zero()
    {
        var broker = new FakeBrokerPublisher();
        var dispatcher = new FakeOutboxDispatcher(broker);
        
        var count = await dispatcher.DispatchAsync(Array.Empty<OutboxMessage>());
        
        count.Should().Be(0);
    }

    [Fact]
    public async Task Reset_Should_Clear_Messages()
    {
        var broker = new FakeBrokerPublisher();
        var dispatcher = new FakeOutboxDispatcher(broker);
        var msgs = new[] { CreateMessage() };
        await dispatcher.DispatchAsync(msgs);
        
        dispatcher.Reset();
        dispatcher.DispatchedMessages.Count.Should().Be(0);
    }

    [Fact]
    public async Task ShouldHaveDispatched_Should_Not_Throw()
    {
        var broker = new FakeBrokerPublisher();
        var dispatcher = new FakeOutboxDispatcher(broker);
        var msgs = new[] { CreateMessage() };
        await dispatcher.DispatchAsync(msgs);
        
        dispatcher.ShouldHaveDispatched(1);
    }

    [Fact]
    public void ShouldHaveDispatched_Should_Throw()
    {
        var broker = new FakeBrokerPublisher();
        var dispatcher = new FakeOutboxDispatcher(broker);
        
        Assert.Throws<InvalidOperationException>(() => dispatcher.ShouldHaveDispatched(1));
    }

    [Fact]
    public void ShouldHaveDispatchedNothing_Should_Not_Throw()
    {
        var broker = new FakeBrokerPublisher();
        var dispatcher = new FakeOutboxDispatcher(broker);
        
        dispatcher.ShouldHaveDispatchedNothing();
    }

    [Fact]
    public void Constructor_WhenBrokerIsNull_ThrowsArgumentNullException()
    {
        var act = () => new FakeOutboxDispatcher(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("broker");
    }

    [Fact]
    public async Task DispatchAsync_WhenMessagesAndRepositoryAreNull_ReturnsZero()
    {
        var broker = new FakeBrokerPublisher();
        var dispatcher = new FakeOutboxDispatcher(broker, repository: null);

        var count = await dispatcher.DispatchAsync(null);

        count.Should().Be(0);
    }

    [Fact]
    public async Task ShouldHaveDispatchedNothing_Should_Throw()
    {
        var broker = new FakeBrokerPublisher();
        var dispatcher = new FakeOutboxDispatcher(broker);
        var msgs = new[] { CreateMessage() };
        await dispatcher.DispatchAsync(msgs);
        
        Assert.Throws<InvalidOperationException>(() => dispatcher.ShouldHaveDispatchedNothing());
    }
}





