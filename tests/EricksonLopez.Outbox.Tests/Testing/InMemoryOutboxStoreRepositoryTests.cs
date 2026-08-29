// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Testing;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

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

    [Fact]
    public async Task FetchPendingAsync_WhenDeliverAtEqualsNow_FetchesMessage()
    {
        var fixedTime = new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);
        var fakeTime = new FakeTimeProvider(fixedTime);
        var sut = new InMemoryOutboxStoreRepository(fakeTime);
        var msg = CreateMessage(deliverAt: fixedTime);
        await sut.InsertAsync(msg, _tx);

        var fetched = await sut.FetchPendingAsync(10);

        fetched.Should().ContainSingle().Which.Id.Should().Be(msg.Id);
    }

    [Fact]
    public async Task ReclaimStaleMessagesAsync_WhenFetchedAtExactlyEqualsThreshold_DoesNotReclaimUntilOlder()
    {
        var fixedTime = new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);
        var fakeTime = new FakeTimeProvider(fixedTime);
        var sut = new InMemoryOutboxStoreRepository(fakeTime);
        var msg = CreateMessage();
        await sut.InsertAsync(msg, _tx);

        // Fetches at fixedTime -> FetchedAt = fixedTime
        await sut.FetchPendingAsync(10);

        // Advance by 10s: threshold = (fixedTime + 10s) - 10s = fixedTime == FetchedAt
        fakeTime.Advance(TimeSpan.FromSeconds(10));
        var reclaimed = await sut.ReclaimStaleMessagesAsync(TimeSpan.FromSeconds(10));
        reclaimed.Should().Be(0);
        sut.GetInFlight().Should().ContainSingle();

        // Advance 1ms: threshold = (fixedTime + 10s + 1ms) - 10s > FetchedAt
        fakeTime.Advance(TimeSpan.FromMilliseconds(1));
        var reclaimed2 = await sut.ReclaimStaleMessagesAsync(TimeSpan.FromSeconds(10));
        reclaimed2.Should().Be(1);
        sut.GetInFlight().Should().BeEmpty();
    }

    [Fact]
    public async Task Reset_ClearsAllFourBuckets()
    {
        var inFlightMsg = CreateMessage();
        var dispatchedMsg = CreateMessage();
        var failedMsg = CreateMessage();
        var pendingMsg = CreateMessage();

        await _sut.InsertAsync(inFlightMsg, _tx);
        await _sut.FetchPendingAsync(10);

        await _sut.InsertAsync(dispatchedMsg, _tx);
        await _sut.MarkAsDispatchedAsync(new[] { dispatchedMsg });

        await _sut.InsertAsync(failedMsg, _tx);
        await _sut.MarkAsFailedAsync(new[] { failedMsg }, "err", true);

        await _sut.InsertAsync(pendingMsg, _tx);

        _sut.GetPending().Should().NotBeEmpty();
        _sut.GetInFlight().Should().NotBeEmpty();
        _sut.GetDispatched().Should().NotBeEmpty();
        _sut.GetFailed().Should().NotBeEmpty();

        _sut.Reset();

        _sut.GetPending().Should().BeEmpty();
        _sut.GetInFlight().Should().BeEmpty();
        _sut.GetDispatched().Should().BeEmpty();
        _sut.GetFailed().Should().BeEmpty();
    }

    [Fact]
    public async Task PurgeDispatchedMessagesAsync_WhenDispatchedProcessedBeforeCutoff_PurgesAndReturnsCount()
    {
        var cutoff = new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero);
        var oldMsg = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, cutoff.AddDays(-10), cutoff.AddDays(-5), null, OutboxMessageStatus.Dispatched, 0, null);
        var newMsg = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, cutoff.AddDays(-10), cutoff.AddDays(1), null, OutboxMessageStatus.Dispatched, 0, null);

        await _sut.InsertAsync(oldMsg, _tx);
        await _sut.InsertAsync(newMsg, _tx);
        await _sut.MarkAsDispatchedAsync(new[] { oldMsg, newMsg });

        var purged = await _sut.PurgeDispatchedMessagesAsync(cutoff, batchSize: 100);

        purged.Should().Be(1);
        _sut.GetDispatched().Should().ContainSingle(m => m.Id == newMsg.Id);
    }

    [Fact]
    public async Task PurgeDispatchedMessagesAsync_WhenProcessedAtIsNull_UsesCreatedAtForCutoffComparison()
    {
        var cutoff = new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero);
        var oldMsg = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, cutoff.AddDays(-5), null, null, OutboxMessageStatus.Dispatched, 0, null);
        var newMsg = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, cutoff.AddDays(1), null, null, OutboxMessageStatus.Dispatched, 0, null);

        await _sut.InsertAsync(oldMsg, _tx);
        await _sut.InsertAsync(newMsg, _tx);
        await _sut.MarkAsDispatchedAsync(new[] { oldMsg, newMsg });

        var purged = await _sut.PurgeDispatchedMessagesAsync(cutoff, batchSize: 100);

        purged.Should().Be(1);
        _sut.GetDispatched().Should().ContainSingle(m => m.Id == newMsg.Id);
    }

    [Fact]
    public async Task PurgeDispatchedMessagesAsync_WithBatchSize_LimitsPurgedCountAndBreaksEarly()
    {
        var cutoff = new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero);
        var msg1 = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, cutoff.AddDays(-5), cutoff.AddDays(-4), null, OutboxMessageStatus.Dispatched, 0, null);
        var msg2 = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, cutoff.AddDays(-5), cutoff.AddDays(-3), null, OutboxMessageStatus.Dispatched, 0, null);
        var msg3 = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, cutoff.AddDays(-5), cutoff.AddDays(-2), null, OutboxMessageStatus.Dispatched, 0, null);

        await _sut.InsertAsync(msg1, _tx);
        await _sut.InsertAsync(msg2, _tx);
        await _sut.InsertAsync(msg3, _tx);
        await _sut.MarkAsDispatchedAsync(new[] { msg1, msg2, msg3 });

        var purged = await _sut.PurgeDispatchedMessagesAsync(cutoff, batchSize: 2);

        purged.Should().Be(2);
        _sut.GetDispatched().Should().HaveCount(1);
    }

    [Fact]
    public async Task PurgeDispatchedMessagesAsync_WhenProcessedAtExactlyEqualsCutoff_DoesNotPurge()
    {
        var cutoff = new DateTimeOffset(2026, 8, 15, 0, 0, 0, TimeSpan.Zero);
        var exactMsg = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, cutoff.AddDays(-1), cutoff, null, OutboxMessageStatus.Dispatched, 0, null);

        await _sut.InsertAsync(exactMsg, _tx);
        await _sut.MarkAsDispatchedAsync(new[] { exactMsg });

        var purged = await _sut.PurgeDispatchedMessagesAsync(cutoff, batchSize: 100);

        purged.Should().Be(0);
        _sut.GetDispatched().Should().ContainSingle(m => m.Id == exactMsg.Id);
    }

    private static OutboxMessage CreateMessage(DateTimeOffset? deliverAt = null)
    {
        return new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, deliverAt, OutboxMessageStatus.Pending, 0, null);
    }
}



