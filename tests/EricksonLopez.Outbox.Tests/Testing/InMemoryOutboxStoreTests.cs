// Copyright © Erickson Lopez. MIT License.
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
        await store.StoreAsync("test1", tx, new OutboxMessageMetadata(null, null, null), null);
        
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

}




