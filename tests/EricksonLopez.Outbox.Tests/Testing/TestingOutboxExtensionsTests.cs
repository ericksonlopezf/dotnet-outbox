using System;
using System.Linq;
using AwesomeAssertions;
using NSubstitute;
using Xunit;
using EricksonLopez.Outbox.Testing;

namespace EricksonLopez.Outbox.Tests.Testing;

public class TestingOutboxExtensionsTests
{
    public class TestMessage { public int Id { get; set; } }

    private static void PublishToStore<TMessage>(InMemoryOutboxStore store, TMessage message) where TMessage : notnull
    {
        store.StoreAsync(message, null!).AsTask().GetAwaiter().GetResult();
    }

    [Fact]
    public void ShouldHavePublished_WithMessages_ReturnsMessages()
    {
        var store = new InMemoryOutboxStore();
        PublishToStore(store, new TestMessage { Id = 1 });

        var results = store.ShouldHavePublished<TestMessage>();
        results.Should().ContainSingle();
    }

    [Fact]
    public void ShouldHavePublished_NoMessages_ThrowsInvalidOperationException()
    {
        var store = new InMemoryOutboxStore();

        Action act = () => store.ShouldHavePublished<TestMessage>();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ShouldHavePublished_WithPredicate_Matches_ReturnsMessages()
    {
        var store = new InMemoryOutboxStore();
        PublishToStore(store, new TestMessage { Id = 1 });
        PublishToStore(store, new TestMessage { Id = 2 });

        var results = store.ShouldHavePublished<TestMessage>(m => m.Id == 1);
        results.Should().ContainSingle().Which.Id.Should().Be(1);
    }

    [Fact]
    public void ShouldHavePublished_WithPredicate_NoMatches_ThrowsInvalidOperationException()
    {
        var store = new InMemoryOutboxStore();
        PublishToStore(store, new TestMessage { Id = 1 });

        Action act = () => store.ShouldHavePublished<TestMessage>(m => m.Id == 2);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ShouldHavePublishedOnce_OneMessage_ReturnsMessage()
    {
        var store = new InMemoryOutboxStore();
        PublishToStore(store, new TestMessage { Id = 1 });

        var result = store.ShouldHavePublishedOnce<TestMessage>();
        result.Id.Should().Be(1);
    }

    [Fact]
    public void ShouldHavePublishedOnce_ZeroOrMultiple_ThrowsInvalidOperationException()
    {
        var store = new InMemoryOutboxStore();
        Action act0 = () => store.ShouldHavePublishedOnce<TestMessage>();
        act0.Should().Throw<InvalidOperationException>();

        PublishToStore(store, new TestMessage { Id = 1 });
        PublishToStore(store, new TestMessage { Id = 2 });
        Action act2 = () => store.ShouldHavePublishedOnce<TestMessage>();
        act2.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ShouldHavePublishedOnce_WithPredicate_OneMatch_ReturnsMessage()
    {
        var store = new InMemoryOutboxStore();
        PublishToStore(store, new TestMessage { Id = 1 });
        PublishToStore(store, new TestMessage { Id = 2 });

        var result = store.ShouldHavePublishedOnce<TestMessage>(m => m.Id == 2);
        result.Id.Should().Be(2);
    }

    [Fact]
    public void ShouldHavePublishedOnce_WithPredicate_ZeroOrMultiple_Throws()
    {
        var store = new InMemoryOutboxStore();
        PublishToStore(store, new TestMessage { Id = 1 });
        PublishToStore(store, new TestMessage { Id = 1 });

        Action act0 = () => store.ShouldHavePublishedOnce<TestMessage>(m => m.Id == 2);
        act0.Should().Throw<InvalidOperationException>();

        Action act2 = () => store.ShouldHavePublishedOnce<TestMessage>(m => m.Id == 1);
        act2.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ShouldHavePublishedTimes_ExactCount_ReturnsMessages()
    {
        var store = new InMemoryOutboxStore();
        PublishToStore(store, new TestMessage { Id = 1 });
        PublishToStore(store, new TestMessage { Id = 2 });

        var results = store.ShouldHavePublishedTimes<TestMessage>(2);
        results.Should().HaveCount(2);
    }

    [Fact]
    public void ShouldHavePublishedTimes_WrongCount_ThrowsInvalidOperationException()
    {
        var store = new InMemoryOutboxStore();
        PublishToStore(store, new TestMessage { Id = 1 });

        Action act = () => store.ShouldHavePublishedTimes<TestMessage>(2);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ShouldNotHavePublished_ZeroMessages_Passes()
    {
        var store = new InMemoryOutboxStore();
        Action act = () => store.ShouldNotHavePublished<TestMessage>();
        act.Should().NotThrow();
    }

    [Fact]
    public void ShouldNotHavePublished_HasMessages_ThrowsInvalidOperationException()
    {
        var store = new InMemoryOutboxStore();
        PublishToStore(store, new TestMessage { Id = 1 });
        
        Action act = () => store.ShouldNotHavePublished<TestMessage>();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ShouldNotHavePublished_WithPredicate_NoMatches_Passes()
    {
        var store = new InMemoryOutboxStore();
        PublishToStore(store, new TestMessage { Id = 1 });
        
        Action act = () => store.ShouldNotHavePublished<TestMessage>(m => m.Id == 2);
        act.Should().NotThrow();
    }

    [Fact]
    public void ShouldNotHavePublished_WithPredicate_HasMatches_ThrowsInvalidOperationException()
    {
        var store = new InMemoryOutboxStore();
        PublishToStore(store, new TestMessage { Id = 1 });
        
        Action act = () => store.ShouldNotHavePublished<TestMessage>(m => m.Id == 1);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void TotalPublishedCount_ReturnsTotalMessages()
    {
        var store = new InMemoryOutboxStore();
        PublishToStore(store, new TestMessage { Id = 1 });
        PublishToStore(store, "string message");

        store.TotalPublishedCount().Should().Be(2);
    }

    // Tester Extensions
    [Fact]
    public void Tester_ShouldNotHavePublished_DelegatesCorrectly()
    {
        var store = new InMemoryOutboxStore();
        var tester = new OutboxTesterImpl(store);

        Action act = () => tester.ShouldNotHavePublished<TestMessage>();
        act.Should().NotThrow();
        
        PublishToStore(store, new TestMessage());
        Action act2 = () => tester.ShouldNotHavePublished<TestMessage>();
        act2.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Tester_ShouldHavePublishedOnce_DelegatesCorrectly()
    {
        var store = new InMemoryOutboxStore();
        var tester = new OutboxTesterImpl(store);

        Action act = () => tester.ShouldHavePublishedOnce<TestMessage>();
        act.Should().Throw<InvalidOperationException>();

        PublishToStore(store, new TestMessage());
        Action act2 = () => tester.ShouldHavePublishedOnce<TestMessage>();
        act2.Should().NotThrow();
    }

    [Fact]
    public void Tester_ShouldHavePublishedOnce_WithPredicate_DelegatesCorrectly()
    {
        var store = new InMemoryOutboxStore();
        var tester = new OutboxTesterImpl(store);

        PublishToStore(store, new TestMessage { Id = 1 });
        
        Action act = () => tester.ShouldHavePublishedOnce<TestMessage>(m => m.Id == 1);
        act.Should().NotThrow();

        Action act2 = () => tester.ShouldHavePublishedOnce<TestMessage>(m => m.Id == 2);
        act2.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Tester_ShouldHavePublished_WithPredicate_DelegatesCorrectly()
    {
        var store = new InMemoryOutboxStore();
        var tester = new OutboxTesterImpl(store);

        PublishToStore(store, new TestMessage { Id = 1 });
        
        Action act = () => tester.ShouldHavePublished<TestMessage>(m => m.Id == 1);
        act.Should().NotThrow();

        Action act2 = () => tester.ShouldHavePublished<TestMessage>(m => m.Id == 2);
        act2.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Tester_ShouldHavePublishedTimes_DelegatesCorrectly()
    {
        var store = new InMemoryOutboxStore();
        var tester = new OutboxTesterImpl(store);

        PublishToStore(store, new TestMessage { Id = 1 });
        
        Action act = () => tester.ShouldHavePublishedTimes<TestMessage>(1);
        act.Should().NotThrow();

        Action act2 = () => tester.ShouldHavePublishedTimes<TestMessage>(2);
        act2.Should().Throw<InvalidOperationException>();
    }
}
