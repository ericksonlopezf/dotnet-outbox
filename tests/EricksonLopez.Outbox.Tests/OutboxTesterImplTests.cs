using System;

using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox.Testing;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Testing;

public class OutboxTesterImplTests
{
    [Fact]
    public async Task ShouldHavePublished_Once_Should_Not_Throw()
    {
        var store = new InMemoryOutboxStore();
        await store.StoreAsync("test1", null!);
        
        var tester = new OutboxTesterImpl(store);
        tester.ShouldHavePublished<string>().Once();
    }

    [Fact]
    public void ShouldHavePublished_Once_Should_Throw()
    {
        var store = new InMemoryOutboxStore();
        var tester = new OutboxTesterImpl(store);
        
        Assert.Throws<InvalidOperationException>(() => tester.ShouldHavePublished<string>().Once());
    }

    [Fact]
    public async Task ShouldHavePublished_Times_Should_Not_Throw()
    {
        var store = new InMemoryOutboxStore();
        await store.StoreAsync("test1", null!);
        await store.StoreAsync("test2", null!);
        
        var tester = new OutboxTesterImpl(store);
        tester.ShouldHavePublished<string>().Times(2);
    }

    [Fact]
    public async Task ShouldHavePublished_Times_Should_Throw()
    {
        var store = new InMemoryOutboxStore();
        await store.StoreAsync("test1", null!);
        
        var tester = new OutboxTesterImpl(store);
        
        Assert.Throws<InvalidOperationException>(() => tester.ShouldHavePublished<string>().Times(2));
    }

    [Fact]
    public async Task ShouldHavePublished_AtLeastOnce_Should_Not_Throw()
    {
        var store = new InMemoryOutboxStore();
        await store.StoreAsync("test1", null!);
        await store.StoreAsync("test2", null!);
        
        var tester = new OutboxTesterImpl(store);
        tester.ShouldHavePublished<string>().AtLeastOnce();
    }

    [Fact]
    public void ShouldHavePublished_AtLeastOnce_Should_Throw()
    {
        var store = new InMemoryOutboxStore();
        var tester = new OutboxTesterImpl(store);
        
        Assert.Throws<InvalidOperationException>(() => tester.ShouldHavePublished<string>().AtLeastOnce());
    }

    [Fact]
    public void ShouldHavePublished_Never_Should_Not_Throw()
    {
        var store = new InMemoryOutboxStore();
        var tester = new OutboxTesterImpl(store);
        
        tester.ShouldHavePublished<string>().Never();
    }

    [Fact]
    public async Task ShouldHavePublished_Never_Should_Throw()
    {
        var store = new InMemoryOutboxStore();
        await store.StoreAsync("test1", null!);
        var tester = new OutboxTesterImpl(store);
        
        Assert.Throws<InvalidOperationException>(() => tester.ShouldHavePublished<string>().Never());
    }

    [Fact]
    public async Task ShouldHavePublished_WithCondition_Should_Not_Throw()
    {
        var store = new InMemoryOutboxStore();
        await store.StoreAsync("test1", null!);
        await store.StoreAsync("test2", null!);
        
        var tester = new OutboxTesterImpl(store);
        tester.ShouldHavePublished<string>().WithCondition(x => x == "test1").Once();
    }
}


