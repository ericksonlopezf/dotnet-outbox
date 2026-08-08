#pragma warning disable CA2012
#pragma warning disable SYSLIB0050
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

namespace EricksonLopez.Outbox.Tests;

public class SingleOutboxMessageListTests
{
    [Fact]
    public async Task ExtensionMethod_MarkAsFailedAsync_Should_Provide_Enumerable_That_Iterates_Correctly()
    {
        var msg = (OutboxMessage)FormatterServices.GetUninitializedObject(typeof(OutboxMessage));
        var repo = Substitute.For<IOutboxRepository>();
        
        IReadOnlyList<OutboxMessage> capturedList = null!;

        repo.MarkAsFailedAsync(Arg.Do<IReadOnlyList<OutboxMessage>>(list => capturedList = list), Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask());

        // Call the extension method
        await repo.MarkAsFailedAsync(msg, "error");

        capturedList.Should().NotBeNull();

        // 1. Generic interface
        using (var enumerator2 = capturedList.GetEnumerator())
        {
            enumerator2.MoveNext().Should().BeTrue();
            enumerator2.Current.Should().Be(msg);
            enumerator2.MoveNext().Should().BeFalse();
            enumerator2.Reset();
            enumerator2.MoveNext().Should().BeTrue();
        }

        // 2. Non-generic interface
        IEnumerable untypedList = capturedList;
        var enumerator3 = untypedList.GetEnumerator();
        enumerator3.MoveNext().Should().BeTrue();
        enumerator3.Current.Should().Be(msg);
        enumerator3.MoveNext().Should().BeFalse();
        enumerator3.Reset();
        enumerator3.MoveNext().Should().BeTrue();
        (enumerator3 as System.IDisposable)?.Dispose();
    }

    [Fact]
    public void GetEnumerator_DuckTyped_ShouldReturnSingleItem()
    {
        var msg = (OutboxMessage)FormatterServices.GetUninitializedObject(typeof(OutboxMessage));
        
        // Use reflection to instantiate SingleOutboxMessageList
        var type = typeof(IOutboxRepository).Assembly.GetType("EricksonLopez.Outbox.Persistence.SingleOutboxMessageList");
        var instance = Activator.CreateInstance(type!, msg);

        var getEnumeratorMethod = type!.GetMethod("GetEnumerator");
        var enumerator = getEnumeratorMethod!.Invoke(instance, null);
        var enumeratorType = enumerator!.GetType();

        var moveNextMethod = enumeratorType.GetMethod("MoveNext");
        var currentProp = enumeratorType.GetProperty("Current");

        // MoveNext = true
        ((bool)moveNextMethod!.Invoke(enumerator, null)!).Should().BeTrue();
        currentProp!.GetValue(enumerator).Should().Be(msg);

        // MoveNext = false
        ((bool)moveNextMethod!.Invoke(enumerator, null)!).Should().BeFalse();
    }

    [Fact]
    public async Task Indexer_ReturnsMessageForIndexZero_ThrowsForOthers()
    {
        var msg = (OutboxMessage)FormatterServices.GetUninitializedObject(typeof(OutboxMessage));
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
    }
}


