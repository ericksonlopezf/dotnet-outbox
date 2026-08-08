using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox.Persistence;
using Xunit;

namespace EricksonLopez.Outbox.Tests;

public class IOutboxRepositoryTests
{
    private sealed class DefaultOutboxRepository : IOutboxRepository
    {
        public ValueTask InsertAsync(OutboxMessage record, IOutboxTransactionContext transaction, CancellationToken cancellationToken = default) => default;
        public ValueTask InsertBatchAsync(ReadOnlyMemory<OutboxMessage> records, IOutboxTransactionContext transaction, CancellationToken cancellationToken = default) => default;
        public ValueTask<IReadOnlyList<OutboxMessage>> FetchPendingAsync(int batchSize, CancellationToken cancellationToken = default) => default;
        public ValueTask MarkAsDispatchedAsync(IReadOnlyList<OutboxMessage> messages, CancellationToken cancellationToken = default) => default;
        public ValueTask MarkAsFailedAsync(IReadOnlyList<OutboxMessage> messages, string error, bool isDeadLetter = false, CancellationToken cancellationToken = default) => default;
        public ValueTask<int> ReclaimStaleMessagesAsync(TimeSpan staleTimeout, CancellationToken cancellationToken = default) => default;
        public ValueTask<long> GetPendingCountAsync(CancellationToken cancellationToken = default) => default;
    }

    [Fact]
    public async Task GetMessageAsync_HasDefaultImplementation_ThrowsNotSupportedException()
    {
        IOutboxRepository repo = new DefaultOutboxRepository();
        var ex = await Record.ExceptionAsync(async () => await repo.GetMessageAsync(Guid.NewGuid()));
        ex.Should().BeOfType<NotSupportedException>();
    }
}
