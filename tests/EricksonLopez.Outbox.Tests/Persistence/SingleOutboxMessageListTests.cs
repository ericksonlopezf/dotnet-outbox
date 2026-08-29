// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Persistence;
using NSubstitute;
using Xunit;

#pragma warning disable CA2012
#pragma warning disable SYSLIB0050
namespace EricksonLopez.Outbox.Tests.Persistence;

public class SingleOutboxMessageListTests
{
    private static OutboxMessage CreateDummyMessage()
    {
        return new OutboxMessage(
            Guid.NewGuid(),
            "test-alias",
            new byte[] { 1, 2, 3 },
            "partition-1",
            "topic-1",
            new byte[] { 4, 5 },
            DateTimeOffset.UtcNow,
            null,
            null,
            OutboxMessageStatus.Pending,
            0,
            null);
    }

    [Fact]
    public async Task ExtensionMethod_MarkAsFailedAsync_Should_Provide_Enumerable_That_Iterates_Correctly()
    {
        var msg = CreateDummyMessage();
        var repo = Substitute.For<IOutboxRepository>();
        
        IReadOnlyList<OutboxMessage> capturedList = null!;

        repo.MarkAsFailedAsync(Arg.Do<IReadOnlyList<OutboxMessage>>(list => capturedList = list), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask());

        // Call the extension method
        await repo.MarkAsFailedAsync(msg, "error", true, CancellationToken.None);

        capturedList.Should().NotBeNull();

        // 1. Generic interface
        using (var enumerator2 = capturedList.GetEnumerator())
        {
            enumerator2.MoveNext().Should().BeTrue();
            enumerator2.Current.Should().Be(msg);
            enumerator2.MoveNext().Should().BeFalse();
            enumerator2.Reset();
            enumerator2.MoveNext().Should().BeTrue();
            enumerator2.Current.Should().Be(msg);
            enumerator2.MoveNext().Should().BeFalse();
        }

        // 2. Non-generic interface
        IEnumerable untypedList = capturedList;
        var enumerator3 = untypedList.GetEnumerator();
        enumerator3.MoveNext().Should().BeTrue();
        enumerator3.Current.Should().Be(msg);
        enumerator3.MoveNext().Should().BeFalse();
        enumerator3.Reset();
        enumerator3.MoveNext().Should().BeTrue();
        enumerator3.Current.Should().Be(msg);
        (enumerator3 as IDisposable)?.Dispose();
    }

    [Fact]
    public void GetEnumerator_DuckTyped_ShouldReturnSingleItem()
    {
        var msg = CreateDummyMessage();
        var list = new SingleOutboxMessageList(msg);
        var enumerator = list.GetEnumerator();

        // MoveNext = true
        enumerator.MoveNext().Should().BeTrue();
        enumerator.Current.Should().Be(msg);

        // MoveNext = false
        enumerator.MoveNext().Should().BeFalse();

        // Reset
        enumerator.Reset();
        enumerator.MoveNext().Should().BeTrue();
        enumerator.Current.Should().Be(msg);

        // Dispose
        enumerator.Dispose();
    }

    [Fact]
    public async Task Indexer_ReturnsMessageForIndexZero_ThrowsForOthers()
    {
        var msg = CreateDummyMessage();
        var repo = Substitute.For<IOutboxRepository>();
        
        IReadOnlyList<OutboxMessage> capturedList = null!;

        repo.MarkAsFailedAsync(Arg.Do<IReadOnlyList<OutboxMessage>>(list => capturedList = list), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask());

        await repo.MarkAsFailedAsync(msg, "error");

        // Index 0
        capturedList[0].Should().Be(msg);

        // Count
        capturedList.Count.Should().Be(1);

        // Index 1
        var ex = Record.Exception(() => capturedList[1]);
        ex.Should().BeOfType<ArgumentOutOfRangeException>();

        // Index -1
        var exNegative = Record.Exception(() => capturedList[-1]);
        exNegative.Should().BeOfType<ArgumentOutOfRangeException>();
    }
}



