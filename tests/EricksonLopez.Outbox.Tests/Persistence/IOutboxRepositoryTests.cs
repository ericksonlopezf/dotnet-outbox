// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Result;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Persistence;

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

    private sealed class ReturningOutboxRepository : IOutboxRepository
    {
        public readonly OutboxMessage ExpectedMessage;

        public ReturningOutboxRepository(OutboxMessage expectedMessage)
        {
            ExpectedMessage = expectedMessage;
        }

        public ValueTask InsertAsync(OutboxMessage record, IOutboxTransactionContext transaction, CancellationToken cancellationToken = default) => default;
        public ValueTask InsertBatchAsync(ReadOnlyMemory<OutboxMessage> records, IOutboxTransactionContext transaction, CancellationToken cancellationToken = default) => default;
        public ValueTask<IReadOnlyList<OutboxMessage>> FetchPendingAsync(int batchSize, CancellationToken cancellationToken = default) => default;
        public ValueTask MarkAsDispatchedAsync(IReadOnlyList<OutboxMessage> messages, CancellationToken cancellationToken = default) => default;
        public ValueTask MarkAsFailedAsync(IReadOnlyList<OutboxMessage> messages, string error, bool isDeadLetter = false, CancellationToken cancellationToken = default) => default;
        public ValueTask<int> ReclaimStaleMessagesAsync(TimeSpan staleTimeout, CancellationToken cancellationToken = default) => default;
        public ValueTask<long> GetPendingCountAsync(CancellationToken cancellationToken = default) => default;

        public ValueTask<OutboxMessage?> GetMessageAsync(Guid id, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<OutboxMessage?>(ExpectedMessage);
        }
    }

    [Fact]
    public async Task GetMessageAsync_NoHint_HasDefaultImplementation_ThrowsNotSupportedException()
    {
        IOutboxRepository repo = new DefaultOutboxRepository();
        var id = Guid.NewGuid();
        var ex = await Record.ExceptionAsync(async () => await repo.GetMessageAsync(id));
        ex.Should().BeOfType<NotSupportedException>();
        ex!.Message.Should().Be("This IOutboxRepository implementation does not support single-message lookup via GetMessageAsync(Guid). Override this method in your IOutboxRepository implementation to enable single-message retrieval. For PostgreSQL: 'SELECT * FROM outbox.messages WHERE id = @Id LIMIT 1;'");
    }

    [Fact]
    public async Task GetMessageAsync_WithCreatedAtHint_DelegatesToNoHintOverload()
    {
        IOutboxRepository repo = new DefaultOutboxRepository();
        var id = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow;
        var ex = await Record.ExceptionAsync(async () => await repo.GetMessageAsync(id, createdAt));
        ex.Should().BeOfType<NotSupportedException>();
        ex!.Message.Should().Contain("GetMessageAsync(Guid)");
    }

    [Fact]
    public async Task GetMessageAsync_WithCreatedAtHint_WhenCustomImplementationProvided_ReturnsResult()
    {
        var expectedMessage = new OutboxMessage(
            Guid.NewGuid(),
            "custom-type",
            new byte[] { 1 },
            null,
            null,
            new byte[] { 2 },
            DateTimeOffset.UtcNow,
            null,
            null,
            OutboxMessageStatus.Dispatched,
            0,
            null);

        IOutboxRepository repo = new ReturningOutboxRepository(expectedMessage);
        var result = await repo.GetMessageAsync(expectedMessage.Id, DateTimeOffset.UtcNow, CancellationToken.None);
        result.Should().BeSameAs(expectedMessage);
    }

    [Fact]
    public async Task PurgeDispatchedMessagesAsync_DefaultImplementation_ReturnsZero()
    {
        IOutboxRepository repo = new DefaultOutboxRepository();
        var cutoff = DateTimeOffset.UtcNow;
        var purged = await repo.PurgeDispatchedMessagesAsync(cutoff, batchSize: 500, CancellationToken.None);
        purged.Should().Be(0);
    }
}




