// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox.Persistence;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Configuration;

public class OutboxExtensionsTests
{
    [Fact]
    public async Task StoreAsync_NullOutbox_ThrowsArgumentNullException()
    {
        IOutbox outbox = null!;
        var messages = new List<string> { "test" };
        var transaction = Substitute.For<IOutboxTransactionContext>();

        Func<Task> act = async () => await outbox.StoreAsync<string>(messages, transaction);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("outbox");
    }

    [Fact]
    public async Task StoreAsync_NullMessages_ThrowsArgumentNullException()
    {
        var outbox = Substitute.For<IOutbox>();
        IEnumerable<string> messages = null!;
        var transaction = Substitute.For<IOutboxTransactionContext>();

        Func<Task> act = async () => await outbox.StoreAsync<string>(messages, transaction);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("messages");
    }

    [Fact]
    public async Task StoreAsync_EmptyMessages_ReturnsCompletedTaskWithoutCallingOutbox()
    {
        var outbox = Substitute.For<IOutbox>();
        IEnumerable<string> messages = Array.Empty<string>();
        var transaction = Substitute.For<IOutboxTransactionContext>();

        await outbox.StoreAsync<string>(messages, transaction);

        await outbox.DidNotReceiveWithAnyArgs().StoreAsync(default(ReadOnlyMemory<string>), default!);
    }

    [Fact]
    public async Task StoreAsync_CollectionMessages_CallsOutboxWithReadOnlyMemory()
    {
        var outbox = Substitute.For<IOutbox>();
        var transaction = Substitute.For<IOutboxTransactionContext>();
        var cancellationToken = new CancellationToken();
        
        var messages = new List<string> { "msg1", "msg2" };
        
        await outbox.StoreAsync<string>(messages, transaction, cancellationToken);

        await outbox.Received(1).StoreAsync(
            Arg.Is<ReadOnlyMemory<string>>(m => CheckMessages(m)),
            transaction,
            cancellationToken);
    }

    [Fact]
    public async Task StoreAsync_EnumerableMessages_CallsOutboxWithReadOnlyMemory()
    {
        var outbox = Substitute.For<IOutbox>();
        var transaction = Substitute.For<IOutboxTransactionContext>();
        var cancellationToken = new CancellationToken();
        
        IEnumerable<string> messages = Enumerable.Range(1, 2).Select(i => $"msg{i}");
        
        await outbox.StoreAsync<string>(messages, transaction, cancellationToken);

        await outbox.Received(1).StoreAsync(
            Arg.Is<ReadOnlyMemory<string>>(m => CheckMessages(m)),
            transaction,
            cancellationToken);
    }

    private static bool CheckMessages(ReadOnlyMemory<string> m)
    {
        var span = m.Span;
        return span.Length == 2 && span[0] == "msg1" && span[1] == "msg2";
    }
}



