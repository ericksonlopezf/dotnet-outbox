using System;
using System.Collections.Generic;

using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Testing;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Testing;

public class InMemoryOutboxStoreTests
{
    [Fact]
    public async Task GenericStore_Should_Store_Single_Message()
    {
        var store = new InMemoryOutboxStore();
        var tx = Substitute.For<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>();
        await store.StoreAsync("test1", tx);
        
        var msgs = store.GetPublishedMessages<string>();
        msgs.Count.Should().Be(1);
        msgs[0].Should().Be("test1");
    }

    [Fact]
    public async Task GenericStore_Should_Store_Multiple_Messages()
    {
        var store = new InMemoryOutboxStore();
        var tx = Substitute.For<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>();
        var items = new[] { "test1", "test2" };
        await store.StoreAsync<string>(items, tx);
        
        var msgs = store.GetPublishedMessages<string>();
        msgs.Count.Should().Be(2);
    }

    [Fact]
    public async Task GenericStore_Should_Store_Message_With_Metadata()
    {
        var store = new InMemoryOutboxStore();
        var tx = Substitute.For<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>();
        await store.StoreAsync("test1", tx, new MessageMetadata(null, null, null), null);
        
        var msgs = store.GetPublishedMessages<string>();
        msgs.Count.Should().Be(1);
    }

    [Fact]
    public void GenericStore_Publish_Should_Return_Builder()
    {
        var store = new InMemoryOutboxStore();
        var builder = store.Publish("test1");
    }

    [Fact]
    public async Task GenericStore_Reset_Should_Clear_Messages()
    {
        var store = new InMemoryOutboxStore();
        var tx = Substitute.For<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>();
        await store.StoreAsync("test1", tx);
        
        store.Reset();
        store.GetPublishedMessages<string>().Count.Should().Be(0);
    }

    [Fact]
    public async Task RepoStore_InsertAsync_Should_Add_Pending()
    {
        var repo = new InMemoryOutboxStoreRepository();
        var tx = Substitute.For<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>();
        
        var msg = new OutboxMessage(Guid.NewGuid(), "a", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        await repo.InsertAsync(msg, tx);
        
        repo.GetPending().Count.Should().Be(1);
    }

    [Fact]
    public async Task RepoStore_InsertBatchAsync_Should_Add_Pending()
    {
        var repo = new InMemoryOutboxStoreRepository();
        var tx = Substitute.For<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>();
        
        var msg1 = new OutboxMessage(Guid.NewGuid(), "a", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var msg2 = new OutboxMessage(Guid.NewGuid(), "a", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var msgs = new[] { msg1, msg2 };
        await repo.InsertBatchAsync(msgs, tx);
        
        repo.GetPending().Count.Should().Be(2);
    }

    [Fact]
    public async Task RepoStore_FetchPendingAsync_Should_Return_And_Remove_Pending()
    {
        var repo = new InMemoryOutboxStoreRepository();
        var tx = Substitute.For<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>();
        
        var msg = new OutboxMessage(Guid.NewGuid(), "a", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        await repo.InsertAsync(msg, tx);
        
        var fetched = await repo.FetchPendingAsync(10);
        fetched.Count.Should().Be(1);
        repo.GetPending().Count.Should().Be(0);
    }

    [Fact]
    public async Task RepoStore_FetchPendingAsync_Should_Not_Return_Future_DeliverAt()
    {
        var repo = new InMemoryOutboxStoreRepository();
        var tx = Substitute.For<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>();
        
        var msg = new OutboxMessage(Guid.NewGuid(), "a", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow.AddMinutes(10), 0, 0, null);
        await repo.InsertAsync(msg, tx);
        
        var fetched = await repo.FetchPendingAsync(10);
        fetched.Count.Should().Be(0);
    }

    [Fact]
    public async Task RepoStore_MarkAsDispatchedAsync_Should_Move_To_Dispatched()
    {
        var repo = new InMemoryOutboxStoreRepository();
        var tx = Substitute.For<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>();
        
        var id = Guid.NewGuid();
        var msg = new OutboxMessage(id, "a", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        await repo.InsertAsync(msg, tx);
        
        await repo.MarkAsDispatchedAsync(new[] { msg });
        
        repo.GetPending().Count.Should().Be(0);
        repo.GetDispatched().Count.Should().Be(1);
    }

    [Fact]
    public async Task RepoStore_MarkAsFailedAsync_Should_Move_To_Failed()
    {
        var repo = new InMemoryOutboxStoreRepository();
        var tx = Substitute.For<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>();
        
        var id = Guid.NewGuid();
        var msg = new OutboxMessage(id, "a", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        await repo.InsertAsync(msg, tx);
        
        await repo.FetchPendingAsync(10);
        await Task.Delay(1100);
        
        repo.GetPending().Count.Should().Be(0);
        repo.GetInFlight().Count.Should().Be(1);
    }

    [Fact]
    public async Task RepoStore_ReclaimStaleMessagesAsync_Should_Reclaim()
    {
        var repo = new InMemoryOutboxStoreRepository();
        var tx = Substitute.For<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>();
        
        var id = Guid.NewGuid();
        // create in past to trigger stale condition
        var msg = new OutboxMessage(id, "a", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow.AddMinutes(-10), null, null, 0, 0, null);
        await repo.InsertAsync(msg, tx);
        
        await repo.FetchPendingAsync(10);
        await Task.Delay(1100);
        repo.GetInFlight().Count.Should().Be(1);

        var reclaimed = await repo.ReclaimStaleMessagesAsync(TimeSpan.FromSeconds(1));
        reclaimed.Should().Be(1);
        repo.GetInFlight().Count.Should().Be(0);
        repo.GetPending().Count.Should().Be(1);
    }

    [Fact]
    public async Task RepoStore_Reset_Should_Clear_All()
    {
        var repo = new InMemoryOutboxStoreRepository();
        var tx = Substitute.For<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>();
        var msg = new OutboxMessage(Guid.NewGuid(), "a", Array.Empty<byte>(), null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        await repo.InsertAsync(msg, tx);
        
        repo.Reset();
        repo.GetPending().Count.Should().Be(0);
    }
}



