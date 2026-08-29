// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Inbox.Core;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Inbox.Tests;

public sealed class IdempotencyCheckerTests
{
    [Fact]
    public void Constructor_NullGuards_ThrowArgumentNullException()
    {
        var store = Substitute.For<IInboxStore>();
        var filter = Substitute.For<IInboxConsumerFilter>();

        Action act1 = () => _ = new IdempotencyChecker(null!, filter);
        act1.Should().Throw<ArgumentNullException>().WithParameterName("inboxStore");

        Action act2 = () => _ = new IdempotencyChecker(store, null!);
        act2.Should().Throw<ArgumentNullException>().WithParameterName("consumerFilter");
    }

    [Fact]
    public async Task NullArguments_ThrowArgumentNullException()
    {
        var store = Substitute.For<IInboxStore>();
        var filter = Substitute.For<IInboxConsumerFilter>();
        var checker = new IdempotencyChecker(store, filter);

        Func<Task> act1 = async () => await checker.HasProcessedAsync(null!, "consumer");
        await act1.Should().ThrowAsync<ArgumentNullException>().WithParameterName("messageId");

        Func<Task> act2 = async () => await checker.HasProcessedAsync("msg", null!);
        await act2.Should().ThrowAsync<ArgumentNullException>().WithParameterName("consumerName");

        Func<Task> act3 = async () => await checker.ExecuteIdempotentlyAsync(null!, "consumer", _ => ValueTask.CompletedTask);
        await act3.Should().ThrowAsync<ArgumentNullException>().WithParameterName("messageId");

        Func<Task> act4 = async () => await checker.ExecuteIdempotentlyAsync("msg", null!, _ => ValueTask.CompletedTask);
        await act4.Should().ThrowAsync<ArgumentNullException>().WithParameterName("consumerName");

        Func<Task> act5 = async () => await checker.ExecuteIdempotentlyAsync("msg", "consumer", null!);
        await act5.Should().ThrowAsync<ArgumentNullException>().WithParameterName("handler");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task HasProcessedAsync_DelegatesToStore_AndReturnsStoreResult(bool storeResult)
    {
        var store = Substitute.For<IInboxStore>();
        var filter = Substitute.For<IInboxConsumerFilter>();
        using var cts = new CancellationTokenSource();

        store.HasBeenProcessedAsync("msg-42", "consumer-alpha", cts.Token)
            .Returns(ValueTask.FromResult(storeResult));

        var checker = new IdempotencyChecker(store, filter);
        var result = await checker.HasProcessedAsync("msg-42", "consumer-alpha", cts.Token);

        result.Should().Be(storeResult);
        _ = store.Received(1).HasBeenProcessedAsync("msg-42", "consumer-alpha", cts.Token);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExecuteIdempotentlyAsync_DelegatesToFilter_AndReturnsFilterResult(bool filterResult)
    {
        var store = Substitute.For<IInboxStore>();
        var filter = Substitute.For<IInboxConsumerFilter>();
        using var cts = new CancellationTokenSource();
        Func<CancellationToken, ValueTask> handler = _ => ValueTask.CompletedTask;

        filter.ExecuteIdempotentlyAsync("msg-42", "consumer-alpha", handler, cts.Token)
            .Returns(ValueTask.FromResult(filterResult));

        var checker = new IdempotencyChecker(store, filter);
        var result = await checker.ExecuteIdempotentlyAsync("msg-42", "consumer-alpha", handler, cts.Token);

        result.Should().Be(filterResult);
        _ = filter.Received(1).ExecuteIdempotentlyAsync("msg-42", "consumer-alpha", handler, cts.Token);
    }
}
