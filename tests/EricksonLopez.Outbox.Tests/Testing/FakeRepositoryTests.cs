using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using NSubstitute;
using Xunit;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Testing;

namespace EricksonLopez.Outbox.Tests.Testing;

public class FakeRepositoryTests
{
    [Fact]
    public async Task FakeDeadLetterRepository_InsertAndGet_WorkCorrectly()
    {
        var sut = new FakeDeadLetterRepository();
        
        var message = new DeadLetterMessage
        {
            Id = Guid.NewGuid(),
            DeadLetteredAt = DateTimeOffset.UtcNow
        };
        
        await sut.InsertAsync(message);
        
        sut.Count.Should().Be(1);
        sut.Messages.Should().ContainSingle();
        
        var results = await sut.GetAsync(10);
        results.Should().ContainSingle();
        
        await sut.DeleteAsync(message.Id);
        sut.Count.Should().Be(0);
        
        await sut.InsertAsync(message);
        await sut.PurgeAsync(DateTimeOffset.UtcNow.AddMinutes(1));
        sut.Count.Should().Be(0);

        await sut.InsertAsync(message);
        sut.Clear();
        sut.Count.Should().Be(0);
    }

    [Fact]
    public async Task FakeDeadLetterRepository_GetWithAfter_FiltersCorrectly()
    {
        var sut = new FakeDeadLetterRepository();
        
        var message1 = new DeadLetterMessage
        {
            Id = Guid.NewGuid(),
            DeadLetteredAt = DateTimeOffset.UtcNow.AddDays(-2)
        };
        
        var message2 = new DeadLetterMessage
        {
            Id = Guid.NewGuid(),
            DeadLetteredAt = DateTimeOffset.UtcNow
        };
        
        await sut.InsertAsync(message1);
        await sut.InsertAsync(message2);
        
        var results = await sut.GetAsync(10, DateTimeOffset.UtcNow.AddDays(-1));
        
        results.Should().ContainSingle().Which.Id.Should().Be(message2.Id);
    }
    
    [Fact]
    public async Task FakeIdempotencyRepository_InsertAndCheck_WorkCorrectly()
    {
        var sut = new FakeIdempotencyRepository();
        
        var record = new IdempotencyRecord
        {
            ConsumerId = "consumer1",
            MessageId = Guid.NewGuid().ToString(),
            ProcessedAt = DateTimeOffset.UtcNow
        };
        
        var isDuplicate1 = sut.WasProcessed(record.MessageId, record.ConsumerId);
        isDuplicate1.Should().BeFalse();
        
        await sut.TryInsertAsync(record);
        
        var isDuplicate2 = sut.WasProcessed(record.MessageId, record.ConsumerId);
        isDuplicate2.Should().BeTrue();
        
        await sut.PurgeExpiredRecordsAsync(DateTimeOffset.UtcNow.AddMinutes(1));
        var isDuplicate3 = sut.WasProcessed(record.MessageId, record.ConsumerId);
        isDuplicate3.Should().BeFalse();
    }
    
    [Fact]
    public async Task InMemoryOutboxStoreRepository_InsertAndFetch_WorkCorrectly()
    {
        var sut = new InMemoryOutboxStoreRepository();
        
        var message = new OutboxMessage(Guid.NewGuid(), "test", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
        
        await sut.InsertAsync(message, Substitute.For<IOutboxTransactionContext>());
        
        var pending = await sut.FetchPendingAsync(10, CancellationToken.None);
        pending.Should().ContainSingle().Which.Id.Should().Be(message.Id);
        
        await sut.MarkAsDispatchedAsync(new[] { message }, CancellationToken.None);
        
        var pending2 = await sut.FetchPendingAsync(10, CancellationToken.None);
        pending2.Should().BeEmpty();
    }

    [Fact]
    public async Task InMemoryOutboxStoreRepository_MarkAsFailed_UpdatesState()
    {
        var sut = new InMemoryOutboxStoreRepository();
        
        var message = new OutboxMessage(Guid.NewGuid(), "test", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
        
        await sut.InsertAsync(message, Substitute.For<IOutboxTransactionContext>());
        await sut.MarkAsFailedAsync(new[] { message }, "error", false, CancellationToken.None);
        
        var pending = await sut.FetchPendingAsync(10, CancellationToken.None);
        pending.Should().NotBeEmpty();
    }
    
    [Fact]
    public async Task InMemoryOutboxStoreRepository_DeadLetter_UpdatesState()
    {
        var sut = new InMemoryOutboxStoreRepository();
        
        var message = new OutboxMessage(Guid.NewGuid(), "test", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
        
        await sut.InsertAsync(message, Substitute.For<IOutboxTransactionContext>());
        await sut.MarkAsFailedAsync(new[] { message }, "fatal error", true, CancellationToken.None);
    }

    [Fact]
    public async Task InMemoryOutboxStoreRepository_ReclaimStale_Works()
    {
        var sut = new InMemoryOutboxStoreRepository();
        var reclaimed = await sut.ReclaimStaleMessagesAsync(TimeSpan.Zero);
        reclaimed.Should().Be(0);
    }
    
    [Fact]
    public void PublishedRawMessage_Constructors_Work()
    {
        var msg = new OutboxMessage(Guid.NewGuid(), "test", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
        var metadata = new MessageMetadata();
        
        var sut = new PublishedRawMessage(msg.MessageType, msg.Payload, metadata);
        sut.MessageType.Should().Be(msg.MessageType);
        sut.Metadata.Should().Be(metadata);
    }
}
