using System;
using System.Collections.Generic;

using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Testing;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Testing;

public class FakeOutboxDispatcherTests
{
    [Fact]
    public async Task DispatchAsync_WithMessages_Should_Dispatch()
    {
        var broker = new FakeBrokerPublisher();
        var dispatcher = new FakeOutboxDispatcher(broker);
        
        var msgs = new[] 
        {
            new OutboxMessage(Guid.NewGuid(), "a", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null)
        };
        
        var count = await dispatcher.DispatchAsync(msgs);
        
        count.Should().Be(1);
        dispatcher.DispatchedMessages.Count.Should().Be(1);
    }

    [Fact]
    public async Task DispatchAsync_WithRepository_Should_Fetch_And_Dispatch()
    {
        var repo = Substitute.For<IOutboxRepository>();
        var msgs = new[] 
        {
            new OutboxMessage(Guid.NewGuid(), "a", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null)
        };
        repo.FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(msgs);
        
        var broker = new FakeBrokerPublisher();
        var dispatcher = new FakeOutboxDispatcher(broker, repo);
        
        var count = await dispatcher.DispatchAsync();
        
        count.Should().Be(1);
        dispatcher.DispatchedMessages.Count.Should().Be(1);
        await repo.Received(1).MarkAsDispatchedAsync(Arg.Any<IReadOnlyList<OutboxMessage>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchAsync_Should_Skip_If_Canceled()
    {
        var broker = new FakeBrokerPublisher();
        var dispatcher = new FakeOutboxDispatcher(broker);
        
        var msgs = new[] 
        {
            new OutboxMessage(Guid.NewGuid(), "a", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null)
        };
        
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
        
        var msgs = new[] 
        {
            new OutboxMessage(Guid.NewGuid(), "a", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null)
        };
        
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
        var msgs = new[] { new OutboxMessage(Guid.NewGuid(), "a", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null) };
        await dispatcher.DispatchAsync(msgs);
        
        dispatcher.Reset();
        dispatcher.DispatchedMessages.Count.Should().Be(0);
    }

    [Fact]
    public async Task ShouldHaveDispatched_Should_Not_Throw()
    {
        var broker = new FakeBrokerPublisher();
        var dispatcher = new FakeOutboxDispatcher(broker);
        var msgs = new[] { new OutboxMessage(Guid.NewGuid(), "a", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null) };
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
    public async Task ShouldHaveDispatchedNothing_Should_Throw()
    {
        var broker = new FakeBrokerPublisher();
        var dispatcher = new FakeOutboxDispatcher(broker);
        var msgs = new[] { new OutboxMessage(Guid.NewGuid(), "a", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null) };
        await dispatcher.DispatchAsync(msgs);
        
        Assert.Throws<InvalidOperationException>(() => dispatcher.ShouldHaveDispatchedNothing());
    }
}


