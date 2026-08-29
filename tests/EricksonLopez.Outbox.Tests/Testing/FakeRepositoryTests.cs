// Copyright © Erickson Lopez. MIT License.
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Testing;
using EricksonLopez.Outbox.Tests.Infrastructure;
using NSubstitute;
using Xunit;

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
        sut.Messages[0].Id.Should().Be(message.Id);
        
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
    public async Task FakeIdempotencyRepository_TryInsertAndCheck_Works()
    {
        var sut = new FakeIdempotencyRepository();
        var record = new IdempotencyRecord
        {
            MessageId = "msg1",
            ConsumerId = "cons1",
            ProcessedAt = DateTimeOffset.UtcNow
        };
        
        var first = await sut.TryInsertAsync(record);
        first.Should().BeTrue();
        
        var second = await sut.TryInsertAsync(record);
        second.Should().BeFalse();
        
        sut.WasProcessed("msg1", "cons1").Should().BeTrue();
        sut.WasProcessed("msg2", "cons1").Should().BeFalse();
        sut.Count.Should().Be(1);
        sut.Records.Should().ContainSingle();

        await sut.PurgeExpiredRecordsAsync(DateTimeOffset.UtcNow.AddMinutes(1));
        sut.Count.Should().Be(0);
        
        await sut.TryInsertAsync(record);
        sut.Clear();
        sut.Count.Should().Be(0);
    }
    
    [Fact]
    public async Task InMemoryOutboxStoreRepository_InsertAndFetch_WorkCorrectly()
    {
        var sut = new InMemoryOutboxStoreRepository();
        
        var message = new OutboxMessageTestDataBuilder().WithMessageType("test").Build();
        
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
        
        var message = new OutboxMessageTestDataBuilder().WithMessageType("test").Build();
        
        await sut.InsertAsync(message, Substitute.For<IOutboxTransactionContext>());
        await sut.MarkAsFailedAsync(new[] { message }, "error", false, CancellationToken.None);
        
        var pending = await sut.FetchPendingAsync(10, CancellationToken.None);
        pending.Should().NotBeEmpty();
    }
    
    [Fact]
    public async Task InMemoryOutboxStoreRepository_DeadLetter_UpdatesState()
    {
        var sut = new InMemoryOutboxStoreRepository();
        
        var message = new OutboxMessageTestDataBuilder().WithMessageType("test").Build();
        
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
        var msg = new OutboxMessageTestDataBuilder().WithMessageType("test").Build();
        var metadata = new OutboxMessageMetadata();
        
        var sut = new PublishedRawMessage(msg.MessageType, msg.Payload, metadata);
        sut.MessageType.Should().Be(msg.MessageType);
        sut.Metadata.Should().Be(metadata);
    }
}

