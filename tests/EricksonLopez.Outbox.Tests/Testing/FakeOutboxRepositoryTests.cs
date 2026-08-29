// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Testing;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Testing;

public class FakeOutboxRepositoryTests
{
    private readonly FakeOutboxRepository _repo = new();
    private readonly IOutboxTransactionContext _tx = Substitute.For<IOutboxTransactionContext>();

    [Fact]
    public void Inner_IsExposed()
    {
        _repo.Inner.Should().NotBeNull();
    }

    [Fact]
    public async Task Methods_DelegateToInner()
    {
        var msg = CreateMessage();

        await _repo.InsertAsync(msg, _tx);
        var pending = await _repo.FetchPendingAsync(10);
        pending.Should().HaveCount(1);
        pending[0].Should().Be(msg);

        await _repo.InsertBatchAsync(new ReadOnlyMemory<OutboxMessage>(new[] { CreateMessage() }), _tx);
        var count = await _repo.GetPendingCountAsync();
        count.Should().Be(1); // the first one is inflight, so pending count is 1.

        await _repo.MarkAsFailedAsync(new[] { msg }, "error", false);
        await _repo.ReclaimStaleMessagesAsync(TimeSpan.Zero);
        
        await _repo.MarkAsDispatchedAsync(new[] { msg });

        var purged = await _repo.PurgeDispatchedMessagesAsync(DateTimeOffset.UtcNow.AddMinutes(1));
        purged.Should().Be(1);

        var fetched = await _repo.GetMessageAsync(msg.Id);
        fetched.Should().BeNull();
    }

    private static OutboxMessage CreateMessage()
    {
        return new OutboxMessage(
            Guid.NewGuid(),
            "Type",
            new byte[] { 1 },
            null,
            null,
            new byte[] { 2 },
            DateTimeOffset.UtcNow,
            null,
            null,
            OutboxMessageStatus.Pending,
            0,
            null
        );
    }
}



