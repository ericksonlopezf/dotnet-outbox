// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Inbox.Core;
using EricksonLopez.Inbox.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Inbox.Tests;

public sealed class DefaultInboxConsumerFilterTests
{
    [Fact]
    public void Constructor_NullStore_ThrowsArgumentNullException()
    {
        Action act = () => _ = new DefaultInboxConsumerFilter(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("inboxStore");
    }

    [Fact]
    public void Constructor_WithLoggerAndTimeProvider_InitializesCorrectly()
    {
        var store = new InMemoryInboxStore();
        var logger = Substitute.For<ILogger<DefaultInboxConsumerFilter>>();
        var fakeTime = new FakeTimeProvider();

        var filter = new DefaultInboxConsumerFilter(store, logger, fakeTime);
        filter.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteIdempotentlyAsync_NullArguments_ThrowArgumentNullException()
    {
        var store = new InMemoryInboxStore();
        var filter = new DefaultInboxConsumerFilter(store);

        Func<Task> act1 = async () => await filter.ExecuteIdempotentlyAsync(null!, "consumer", _ => ValueTask.CompletedTask);
        await act1.Should().ThrowAsync<ArgumentNullException>().WithParameterName("messageId");

        Func<Task> act2 = async () => await filter.ExecuteIdempotentlyAsync("msg-1", null!, _ => ValueTask.CompletedTask);
        await act2.Should().ThrowAsync<ArgumentNullException>().WithParameterName("consumerName");

        Func<Task> act3 = async () => await filter.ExecuteIdempotentlyAsync("msg-1", "consumer", null!);
        await act3.Should().ThrowAsync<ArgumentNullException>().WithParameterName("handler");
    }

    [Fact]
    public async Task ExecuteIdempotentlyAsync_FirstExecution_PassesExactTimestampAndCancellationToken()
    {
        var store = Substitute.For<IInboxStore>();
        var fakeTime = new FakeTimeProvider();
        var fixedTime = new DateTimeOffset(2026, 8, 23, 15, 30, 0, TimeSpan.Zero);
        fakeTime.SetUtcNow(fixedTime);

        store.TryRecordAsync(Arg.Any<IInboxEntry>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(true));

        var filter = new DefaultInboxConsumerFilter(store, timeProvider: fakeTime);
        using var cts = new CancellationTokenSource();
        var handlerInvokedWithToken = false;

        var result = await filter.ExecuteIdempotentlyAsync(
            "order-100",
            "invoice-gen",
            ct =>
            {
                handlerInvokedWithToken = (ct == cts.Token);
                return ValueTask.CompletedTask;
            },
            cts.Token);

        result.Should().BeTrue();
        handlerInvokedWithToken.Should().BeTrue();

        _ = store.Received(1).TryRecordAsync(
            Arg.Is<IInboxEntry>(e =>
                e.MessageId == "order-100" &&
                e.ConsumerName == "invoice-gen" &&
                e.ProcessedAt == fixedTime),
            cts.Token);
    }

    [Fact]
    public async Task ExecuteIdempotentlyAsync_DuplicateMessage_LogsWarningAndReturnsFalseWithoutHandlerInvocation()
    {
        var store = Substitute.For<IInboxStore>();
        store.TryRecordAsync(Arg.Any<IInboxEntry>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(false));

        var logger = Substitute.For<ILogger<DefaultInboxConsumerFilter>>();
        var filter = new DefaultInboxConsumerFilter(store, logger);
        var handlerCalled = false;

        var result = await filter.ExecuteIdempotentlyAsync(
            "order-100",
            "invoice-gen",
            _ =>
            {
                handlerCalled = true;
                return ValueTask.CompletedTask;
            });

        result.Should().BeFalse();
        handlerCalled.Should().BeFalse();

        logger.Received(1).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Duplicate message detected in inbox: MessageId='order-100', Consumer='invoice-gen'")),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }
}
