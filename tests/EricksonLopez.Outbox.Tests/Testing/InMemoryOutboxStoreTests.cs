using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Testing;
using EricksonLopez.Outbox.Persistence;
using NSubstitute;

namespace EricksonLopez.Outbox.Tests.Testing;

public class InMemoryOutboxStoreRepositoryTests
{
    private readonly InMemoryOutboxStoreRepository _sut;
    private readonly IOutboxTransactionContext _tx;

    public InMemoryOutboxStoreRepositoryTests()
    {
        _sut = new InMemoryOutboxStoreRepository();
        _tx = Substitute.For<IOutboxTransactionContext>();
    }

    [Fact]
    public async Task InsertAsync_AddsToPending()
    {
        var msg = CreateMessage();
        await _sut.InsertAsync(msg, _tx);
        _sut.GetPending().Should().ContainSingle().Which.Id.Should().Be(msg.Id);
    }

    [Fact]
    public async Task InsertBatchAsync_AddsAllToPending()
    {
        var msg1 = CreateMessage();
        var msg2 = CreateMessage();
        await _sut.InsertBatchAsync(new[] { msg1, msg2 }, _tx);
        _sut.GetPending().Should().HaveCount(2);
    }

    [Fact]
    public async Task FetchPendingAsync_MovesToInFlight_AndRespectsDeliverAt()
    {
        var msg1 = CreateMessage(deliverAt: DateTimeOffset.UtcNow.AddMinutes(-5));
        var msg2 = CreateMessage(deliverAt: DateTimeOffset.UtcNow.AddMinutes(5));
        var msg3 = CreateMessage(); // null DeliverAt
        
        await _sut.InsertBatchAsync(new[] { msg1, msg2, msg3 }, _tx);
        
        var fetched = await _sut.FetchPendingAsync(10);
        
        fetched.Should().HaveCount(2);
        fetched.Should().Contain(m => m.Id == msg1.Id);
        fetched.Should().Contain(m => m.Id == msg3.Id);
        
        _sut.GetInFlight().Should().HaveCount(2);
        _sut.GetPending().Should().HaveCount(1); // msg2 is still pending
    }

    [Fact]
    public async Task MarkAsDispatchedAsync_MovesInFlightToDispatched()
    {
        var msg = CreateMessage();
        await _sut.InsertAsync(msg, _tx);
        await _sut.FetchPendingAsync(10);
        
        await _sut.MarkAsDispatchedAsync(new[] { msg });
        
        _sut.GetInFlight().Should().BeEmpty();
        _sut.GetDispatched().Should().ContainSingle().Which.Id.Should().Be(msg.Id);
    }

    [Fact]
    public async Task MarkAsDispatchedAsync_MovesPendingToDispatched_IfNotInFlight()
    {
        var msg = CreateMessage();
        await _sut.InsertAsync(msg, _tx);
        
        await _sut.MarkAsDispatchedAsync(new[] { msg });
        
        _sut.GetPending().Should().BeEmpty();
        _sut.GetDispatched().Should().ContainSingle().Which.Id.Should().Be(msg.Id);
    }

    [Fact]
    public async Task MarkAsFailedAsync_MovesInFlightToFailed_IfDeadLetter()
    {
        var msg = CreateMessage();
        await _sut.InsertAsync(msg, _tx);
        await _sut.FetchPendingAsync(10);
        
        await _sut.MarkAsFailedAsync(new[] { msg }, "error", isDeadLetter: true);
        
        _sut.GetInFlight().Should().BeEmpty();
        _sut.GetFailed().Should().ContainSingle().Which.Id.Should().Be(msg.Id);
    }

    [Fact]
    public async Task MarkAsFailedAsync_MovesInFlightToPending_IfNotDeadLetter()
    {
        var msg = CreateMessage();
        await _sut.InsertAsync(msg, _tx);
        await _sut.FetchPendingAsync(10);
        
        await _sut.MarkAsFailedAsync(new[] { msg }, "error", isDeadLetter: false);
        
        _sut.GetInFlight().Should().BeEmpty();
        _sut.GetPending().Should().ContainSingle().Which.Id.Should().Be(msg.Id);
    }

    [Fact]
    public async Task MarkAsFailedAsync_MovesPendingToFailed_IfDeadLetterAndNotInFlight()
    {
        var msg = CreateMessage();
        await _sut.InsertAsync(msg, _tx);
        
        await _sut.MarkAsFailedAsync(new[] { msg }, "error", isDeadLetter: true);
        
        _sut.GetPending().Should().BeEmpty();
        _sut.GetFailed().Should().ContainSingle().Which.Id.Should().Be(msg.Id);
    }

    [Fact]
    public async Task MarkAsFailedAsync_KeepsInPending_IfNotDeadLetterAndNotInFlight()
    {
        var msg = CreateMessage();
        await _sut.InsertAsync(msg, _tx);
        
        await _sut.MarkAsFailedAsync(new[] { msg }, "error", isDeadLetter: false);
        
        _sut.GetPending().Should().ContainSingle().Which.Id.Should().Be(msg.Id);
    }

    [Fact]
    public async Task ReclaimStaleMessagesAsync_ReclaimsStale_IgnoresFresh()
    {
        var msg1 = CreateMessage();
        var msg2 = CreateMessage();
        
        await _sut.InsertBatchAsync(new[] { msg1, msg2 }, _tx);
        await _sut.FetchPendingAsync(10); // Both are in-flight now with FetchAt = UtcNow
        
        // Wait a tiny bit to simulate time passing? Not needed, we can just manipulate staleTimeout
        // If staleTimeout is -1 hour, threshold is UtcNow + 1 hour, so FetchAt is < threshold (stale)
        // Wait, threshold = DateTimeOffset.UtcNow - staleTimeout. 
        // If staleTimeout = -TimeSpan.FromHours(1), threshold is UtcNow + 1h. FetchAt is < threshold -> RECLAIMED
        // If staleTimeout = TimeSpan.FromHours(1), threshold is UtcNow - 1h. FetchAt is > threshold -> KEPT
        
        // Test reclaiming msg1 by manipulating the internal state or just mocking time?
        // Let's just use negative timeout to reclaim everything
        var reclaimedCount = await _sut.ReclaimStaleMessagesAsync(TimeSpan.FromHours(-1));
        
        reclaimedCount.Should().Be(2);
        _sut.GetInFlight().Should().BeEmpty();
        _sut.GetPending().Should().HaveCount(2);
        
        // Now test keeping them
        await _sut.FetchPendingAsync(10);
        var zeroReclaimed = await _sut.ReclaimStaleMessagesAsync(TimeSpan.FromHours(1));
        zeroReclaimed.Should().Be(0);
        _sut.GetInFlight().Should().HaveCount(2);
    }

    [Fact]
    public async Task GetPendingCountAsync_ReturnsCount()
    {
        await _sut.InsertAsync(CreateMessage(), _tx);
        await _sut.InsertAsync(CreateMessage(), _tx);
        
        (await _sut.GetPendingCountAsync()).Should().Be(2);
    }

    [Fact]
    public async Task GetMessageAsync_ReturnsFromAnyBucket()
    {
        var pendingMsg = CreateMessage();
        var inFlightMsg = CreateMessage();
        var dispatchedMsg = CreateMessage();
        var failedMsg = CreateMessage();
        var missingMsg = CreateMessage();
        
        await _sut.InsertAsync(pendingMsg, _tx);
        
        await _sut.InsertAsync(inFlightMsg, _tx);
        await _sut.FetchPendingAsync(10); // now in-flight (wait, pendingMsg was also fetched)
        // Reset and do one by one to ensure buckets
        _sut.Reset();
        
        await _sut.InsertAsync(pendingMsg, _tx);
        
        await _sut.InsertAsync(inFlightMsg, _tx);
        await _sut.FetchPendingAsync(1); // fetch inFlightMsg because it's first? actually pendingMsg is first. 
        // Better: use internal lists via methods
        
        // Let's manipulate via API
        _sut.Reset();
        await _sut.InsertAsync(inFlightMsg, _tx);
        await _sut.FetchPendingAsync(10);
        
        await _sut.InsertAsync(dispatchedMsg, _tx);
        await _sut.MarkAsDispatchedAsync(new[] { dispatchedMsg });
        
        await _sut.InsertAsync(failedMsg, _tx);
        await _sut.MarkAsFailedAsync(new[] { failedMsg }, "err", true);
        
        await _sut.InsertAsync(pendingMsg, _tx);
        
        (await _sut.GetMessageAsync(pendingMsg.Id)).Should().BeEquivalentTo(pendingMsg);
        (await _sut.GetMessageAsync(inFlightMsg.Id)).Should().BeEquivalentTo(inFlightMsg);
        (await _sut.GetMessageAsync(dispatchedMsg.Id)).Should().BeEquivalentTo(dispatchedMsg);
        (await _sut.GetMessageAsync(failedMsg.Id)).Should().BeEquivalentTo(failedMsg);
        (await _sut.GetMessageAsync(missingMsg.Id)).Should().BeNull();
    }

    private static OutboxMessage CreateMessage(DateTimeOffset? deliverAt = null)
    {
        return new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, deliverAt, OutboxMessageStatus.Pending, 0, null);
    }
}
