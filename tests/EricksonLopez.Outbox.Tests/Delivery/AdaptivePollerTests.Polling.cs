// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Diagnostics;
using EricksonLopez.Outbox.Dispatcher;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

#pragma warning disable CA2012
namespace EricksonLopez.Outbox.Tests.Delivery;

public partial class AdaptivePollerTests
{
    [Fact]
    public async Task StartPollingAsync_WhenMessagesAvailable_FetchesAndWritesToChannel()
    {
        using var harness = new TestDispatcherHarness();
        harness.DispatcherOptions.UseAdaptivePolling = false;
        harness.DispatcherOptions.PollingInterval = TimeSpan.FromMilliseconds(10);

        var message = new OutboxMessageTestDataBuilder().WithMessageType("Test").Build();
        using var cts = new CancellationTokenSource();
        harness.Repository.FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<OutboxMessage>>(new List<OutboxMessage> { message }));

        harness.Publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(_ =>
            {
                cts.Cancel();
                return DispatchResult.Ok();
            });

        var channel = harness.CreateChannel();
        var poller = harness.CreatePoller(channel);

        var pollerTask = poller.StartPollingAsync(cts.Token);
        var channelTask = channel.ProcessMessagesAsync(cts.Token);

        try { await Task.WhenAll(pollerTask, channelTask); } catch (OperationCanceledException) { }

        await harness.Repository.Received().FetchPendingAsync(harness.DispatcherOptions.BatchSize, Arg.Any<CancellationToken>());
        await harness.Publisher.Received().PublishRawAsync(message, Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>());
    }

    [Fact]
    public async Task StartPollingAsync_WhenCancelledDuringDelay_BreaksGracefully()
    {
        using var harness = new TestDispatcherHarness();
        var fakeTime = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        harness.DispatcherOptions.UseAdaptivePolling = false;
        harness.DispatcherOptions.PollingInterval = TimeSpan.FromSeconds(5);
        
        var fetchTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        harness.Repository.FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => {
                fetchTcs.TrySetResult();
                return new ValueTask<IReadOnlyList<OutboxMessage>>(Array.Empty<OutboxMessage>());
            });

        var channel = harness.CreateChannel();
        var poller = harness.CreatePoller(channel, fakeTime);

        using var cts = new CancellationTokenSource();
        var task = poller.StartPollingAsync(cts.Token);
        
        await fetchTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();
        
        var act = async () => await task;
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StartPollingAsync_WhenCancelledDuringFetch_BreaksIterationEarly()
    {
        using var harness = new TestDispatcherHarness();
        harness.DispatcherOptions.UseAdaptivePolling = false;
        harness.DispatcherOptions.PollingInterval = TimeSpan.FromSeconds(5);

        var message1 = new OutboxMessageTestDataBuilder().WithMessageType("Test1").Build();
        var message2 = new OutboxMessageTestDataBuilder().WithMessageType("Test2").Build();

        var cts = new CancellationTokenSource();
        harness.Repository.FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<OutboxMessage>>(new List<OutboxMessage> { message1, message2 }));

        harness.Repository.When(x => x.FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()))
            .Do(_ => cts.Cancel());

        var channel = harness.CreateChannel();
        var poller = harness.CreatePoller(channel);

        try { await poller.StartPollingAsync(cts.Token); } catch (OperationCanceledException) { }

        channel.Complete();
        await channel.ProcessMessagesAsync(CancellationToken.None);
        await harness.Publisher.DidNotReceive().PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>());
    }

    [Fact]
    public async Task StartPollingAsync_WhenCancelledDuringMessageProcessing_BreaksEarly()
    {
        using var harness = new TestDispatcherHarness();
        harness.DispatcherOptions.UseAdaptivePolling = false;
        harness.DispatcherOptions.PollingInterval = TimeSpan.FromMilliseconds(50);

        var msg1 = new OutboxMessageTestDataBuilder().WithMessageType("Type1").Build();
        var msg2 = new OutboxMessageTestDataBuilder().WithMessageType("Type2").Build();

        using var cts = new CancellationTokenSource();
        harness.Repository.FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => {
                cts.Cancel();
                return ValueTask.FromResult<IReadOnlyList<OutboxMessage>>(new[] { msg1, msg2 });
            });

        var channel = harness.CreateChannel();
        var poller = harness.CreatePoller(channel);

        try { await poller.StartPollingAsync(cts.Token); } catch (OperationCanceledException) { }

        channel.Complete();
        await channel.ProcessMessagesAsync(CancellationToken.None);
        await harness.Publisher.DidNotReceive().PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>());
    }

    [Fact]
    public async Task StartPollingAsync_WhenMessagesFetched_WritesToChannel()
    {
        using var harness = new TestDispatcherHarness();
        harness.DispatcherOptions.UseAdaptivePolling = false;
        harness.DispatcherOptions.BatchSize = 10;
        harness.DispatcherOptions.PollingInterval = TimeSpan.FromMilliseconds(50);

        var msg = new OutboxMessageTestDataBuilder().WithMessageType("Type").Build();
        harness.Repository.FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IReadOnlyList<OutboxMessage>>(new[] { msg }));

        using var cts = new CancellationTokenSource();
        harness.Publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(_ =>
            {
                cts.Cancel();
                return DispatchResult.Ok();
            });

        var channel = harness.CreateChannel();
        var poller = harness.CreatePoller(channel);

        var pollerTask = poller.StartPollingAsync(cts.Token);
        var channelTask = channel.ProcessMessagesAsync(cts.Token);

        try { await Task.WhenAll(pollerTask, channelTask); } catch (OperationCanceledException) { }

        await harness.Publisher.Received(1).PublishRawAsync(Arg.Is<OutboxMessage>(m => m.Id == msg.Id), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>());
    }

    [Fact]
    public async Task ProcessMessagesAsync_WhenCancellationRequested_BreaksEarly()
    {
        using var harness = new TestDispatcherHarness();
        harness.DispatcherOptions.UseAdaptivePolling = false;
        harness.DispatcherOptions.BatchSize = 10;
        harness.DispatcherOptions.PollingInterval = TimeSpan.FromMilliseconds(50);

        var msg1 = new OutboxMessageTestDataBuilder().WithMessageType("Type1").Build();
        var msg2 = new OutboxMessageTestDataBuilder().WithMessageType("Type2").Build();

        var cts = new CancellationTokenSource();
        harness.Repository.FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => {
                cts.Cancel();
                return ValueTask.FromResult<IReadOnlyList<OutboxMessage>>(new[] { msg1, msg2 });
            });

        var channel = harness.CreateChannel();
        var poller = harness.CreatePoller(channel);

        channel.Complete();
        await channel.ProcessMessagesAsync(CancellationToken.None);
        await harness.Publisher.DidNotReceive().PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>());
    }

    [Fact]
    public async Task StartPollingAsync_WhenTokenAlreadyCancelled_ExitsImmediatelyWithoutProcessing()
    {
        using var harness = new TestDispatcherHarness();
        var channel = harness.CreateChannel();
        var poller = harness.CreatePoller(channel);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await poller.StartPollingAsync(cts.Token);
        await act.Should().NotThrowAsync();

        await harness.Repository.DidNotReceive().FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
        await harness.Publisher.DidNotReceive().PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>());
    }

    [Fact]
    public async Task StartPollingAsync_WhenCancelledDuringProcessMessages_BreaksAndDoesNotWriteRemainingMessages()
    {
        var services = new ServiceCollection();
        var repo = Substitute.For<IOutboxRepository>();
        var msg1 = new OutboxMessage(Guid.NewGuid(), "Type1", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
        var msg2 = new OutboxMessage(Guid.NewGuid(), "Type2", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);

        var cts = new CancellationTokenSource();
        repo.FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                cts.Cancel();
                return ValueTask.FromResult<IReadOnlyList<OutboxMessage>>(new[] { msg1, msg2 });
            });

        services.AddScoped(_ => repo);
        var provider = services.BuildServiceProvider();

        var options = new OutboxDispatcherOptions
        {
            UseAdaptivePolling = false,
            PollingInterval = TimeSpan.FromMilliseconds(10)
        };
        var optionsMock = Options.Create(options);

        var publisher = Substitute.For<IBrokerPublisher>();
        var channel = new OutboxChannel(
            NullLogger<OutboxChannel>.Instance,
            publisher,
            optionsMock,
            Options.Create(new OutboxRuntimeOptions()),
            new OutboxMetrics(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<IErrorSanitizer>(),
            TimeProvider.System);

        var poller = new AdaptivePoller(provider, channel, optionsMock, NullLogger<AdaptivePoller>.Instance, new OutboxMetrics(), TimeProvider.System);

        try
        {
            await poller.StartPollingAsync(cts.Token);
        }
        catch (OperationCanceledException) { }

        channel.Complete();
        bool hasItem = channel.GetType().GetField("_channel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(channel) is System.Threading.Channels.Channel<OutboxMessage> ch && ch.Reader.TryRead(out _);
        hasItem.Should().BeFalse();
    }

    [Fact]
    public async Task StartPollingAsync_WhenAdaptiveWithMaxBatchesPerSecond_EnforcesRateLimitDelay()
    {
        var services = new ServiceCollection();
        var repo = Substitute.For<IOutboxRepository>();
        int fetchCount = 0;
        var secondFetchTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var message = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
        var fullBatch = new List<OutboxMessage> { message, message };

        var fakeTime = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();

        repo.FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var count = Interlocked.Increment(ref fetchCount);
                if (count >= 2) secondFetchTcs.TrySetResult(true);
                if (count == 1)
                {
                    // Simulate 50ms database latency during fetch: elapsedMs will be 50ms
                    fakeTime.Advance(TimeSpan.FromMilliseconds(50));
                    return ValueTask.FromResult<IReadOnlyList<OutboxMessage>>(fullBatch);
                }
                return ValueTask.FromResult<IReadOnlyList<OutboxMessage>>(Array.Empty<OutboxMessage>());
            });

        services.AddScoped(_ => repo);
        var provider = services.BuildServiceProvider();

        var options = new OutboxDispatcherOptions
        {
            UseAdaptivePolling = true,
            BatchSize = 2,
            MaxBatchesPerSecond = 5, // minMs = 1000 / 5 = 200ms. With 50ms elapsed, delayMs = 200 - 50 = 150ms
            PollingInterval = TimeSpan.FromSeconds(10)
        };
        var optionsMock = Options.Create(options);

        var publisher = Substitute.For<IBrokerPublisher>();
        var channel = new OutboxChannel(
            NullLogger<OutboxChannel>.Instance,
            publisher,
            optionsMock,
            Options.Create(new OutboxRuntimeOptions()),
            new OutboxMetrics(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<IErrorSanitizer>(),
            fakeTime);

        var poller = new AdaptivePoller(provider, channel, optionsMock, NullLogger<AdaptivePoller>.Instance, new OutboxMetrics(), fakeTime);

        using var cts = new CancellationTokenSource();
        var pollingTask = poller.StartPollingAsync(cts.Token);

        await Task.Yield();
        Volatile.Read(ref fetchCount).Should().Be(1);

        // Advancing 20ms: with expected 150ms delay, 20ms < 150ms so second fetch must NOT have happened.
        // If mutated to default 10ms delay (e.g. MaxBatchesPerSecond < 0 mutation), 20ms would fire the timer.
        fakeTime.Advance(TimeSpan.FromMilliseconds(20));
        await Task.Yield();
        secondFetchTcs.Task.IsCompleted.Should().BeFalse();

        // Advancing another 140ms (total 160ms >= 150ms): second fetch must now trigger
        fakeTime.Advance(TimeSpan.FromMilliseconds(140));
        await secondFetchTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Volatile.Read(ref fetchCount).Should().Be(2);

        cts.Cancel();
        try { await pollingTask; } catch (OperationCanceledException) { }
    }
}




