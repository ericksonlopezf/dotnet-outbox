// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Inbox.Storage;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace EricksonLopez.Inbox.Tests;

public sealed class InMemoryInboxStoreTests
{
    [Fact]
    public void Constructor_WithTimeProvider_InitializesProperly()
    {
        var fakeTime = new FakeTimeProvider();
        var store = new InMemoryInboxStore(fakeTime);
        store.Count.Should().Be(0);
        store.TimeProvider.Should().BeSameAs(fakeTime);

        var defaultStore = new InMemoryInboxStore();
        defaultStore.Count.Should().Be(0);
        defaultStore.TimeProvider.Should().BeSameAs(TimeProvider.System);
    }

    [Fact]
    public async Task TryRecordAsync_NullArguments_ThrowArgumentNullException()
    {
        var store = new InMemoryInboxStore();

        Func<Task> act1 = async () => await store.TryRecordAsync(null!);
        await act1.Should().ThrowAsync<ArgumentNullException>().WithParameterName("entry");

        Func<Task> act2 = async () => await store.TryRecordAsync(new InboxEntry(null!, "consumer", DateTimeOffset.UtcNow));
        await act2.Should().ThrowAsync<ArgumentNullException>().WithParameterName("entry.MessageId");

        Func<Task> act3 = async () => await store.TryRecordAsync(new InboxEntry("msg-1", null!, DateTimeOffset.UtcNow));
        await act3.Should().ThrowAsync<ArgumentNullException>().WithParameterName("entry.ConsumerName");
    }

    [Fact]
    public async Task HasBeenProcessedAsync_NullArguments_ThrowArgumentNullException()
    {
        var store = new InMemoryInboxStore();

        Func<Task> act1 = async () => await store.HasBeenProcessedAsync(null!, "consumer");
        await act1.Should().ThrowAsync<ArgumentNullException>().WithParameterName("messageId");

        Func<Task> act2 = async () => await store.HasBeenProcessedAsync("msg", null!);
        await act2.Should().ThrowAsync<ArgumentNullException>().WithParameterName("consumerName");
    }

    [Fact]
    public async Task TryRecordAsync_NewEntry_ReturnsTrue()
    {
        var store = new InMemoryInboxStore();
        var entry = new InboxEntry("msg-1", "consumer-A", DateTimeOffset.UtcNow);

        var result = await store.TryRecordAsync(entry, CancellationToken.None);

        result.Should().BeTrue();
        store.Count.Should().Be(1);
    }

    [Fact]
    public async Task TryRecordAsync_DuplicateEntry_ReturnsFalse()
    {
        var store = new InMemoryInboxStore();
        var entry = new InboxEntry("msg-1", "consumer-A", DateTimeOffset.UtcNow);

        var first = await store.TryRecordAsync(entry);
        var second = await store.TryRecordAsync(entry);

        first.Should().BeTrue();
        second.Should().BeFalse();
        store.Count.Should().Be(1);
    }

    [Fact]
    public async Task TryRecordAsync_SameMessageDifferentConsumer_ReturnsTrue()
    {
        var store = new InMemoryInboxStore();
        var entry1 = new InboxEntry("msg-1", "consumer-A", DateTimeOffset.UtcNow);
        var entry2 = new InboxEntry("msg-1", "consumer-B", DateTimeOffset.UtcNow);

        var first = await store.TryRecordAsync(entry1);
        var second = await store.TryRecordAsync(entry2);

        first.Should().BeTrue();
        second.Should().BeTrue();
        store.Count.Should().Be(2);
    }

    [Fact]
    public async Task HasBeenProcessedAsync_WhenPresent_ReturnsTrue()
    {
        var store = new InMemoryInboxStore();
        var entry = new InboxEntry("msg-100", "order-handler", DateTimeOffset.UtcNow);
        await store.TryRecordAsync(entry);

        var exists = await store.HasBeenProcessedAsync("msg-100", "order-handler", CancellationToken.None);
        var missing = await store.HasBeenProcessedAsync("msg-999", "order-handler", CancellationToken.None);

        exists.Should().BeTrue();
        missing.Should().BeFalse();
    }

    [Fact]
    public async Task PurgeExpiredEntriesAsync_RemovesStrictlyOlderEntries_PreservingBoundaryAndNewer()
    {
        var store = new InMemoryInboxStore();
        var threshold = new DateTimeOffset(2026, 8, 23, 10, 0, 0, TimeSpan.Zero);

        var strictlyOlder = new InboxEntry("older-msg", "consumer-A", threshold.AddSeconds(-1));
        var exactlyAtThreshold = new InboxEntry("exact-msg", "consumer-A", threshold);
        var strictlyNewer = new InboxEntry("newer-msg", "consumer-A", threshold.AddSeconds(1));

        await store.TryRecordAsync(strictlyOlder);
        await store.TryRecordAsync(exactlyAtThreshold);
        await store.TryRecordAsync(strictlyNewer);

        store.Count.Should().Be(3);

        await store.PurgeExpiredEntriesAsync(threshold, CancellationToken.None);

        store.Count.Should().Be(2);
        (await store.HasBeenProcessedAsync("older-msg", "consumer-A")).Should().BeFalse();
        (await store.HasBeenProcessedAsync("exact-msg", "consumer-A")).Should().BeTrue();
        (await store.HasBeenProcessedAsync("newer-msg", "consumer-A")).Should().BeTrue();
    }

    [Fact]
    public async Task Clear_RemovesAllEntries()
    {
        var store = new InMemoryInboxStore();
        await store.TryRecordAsync(new InboxEntry("msg-1", "consumer-A", DateTimeOffset.UtcNow));
        await store.TryRecordAsync(new InboxEntry("msg-2", "consumer-A", DateTimeOffset.UtcNow));
        store.Count.Should().Be(2);

        store.Clear();
        store.Count.Should().Be(0);
    }
}
