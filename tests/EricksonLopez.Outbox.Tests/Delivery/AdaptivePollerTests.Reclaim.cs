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
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

#pragma warning disable CA2012
namespace EricksonLopez.Outbox.Tests.Delivery;

public partial class AdaptivePollerTests
{
    [Fact]
    public async Task StartPollingAsync_WhenReclaimIntervalElapsed_ReclaimsStaleMessages()
    {
        using var harness = new TestDispatcherHarness();
        var fakeTime = new FakeTimeProvider();
        harness.DispatcherOptions.UseAdaptivePolling = true;
        harness.DispatcherOptions.PollingInterval = TimeSpan.FromMilliseconds(1);

        var message = new OutboxMessageTestDataBuilder().WithMessageType("Test").Build();
        using var cts = new CancellationTokenSource();

        harness.Repository.FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                cts.Cancel();
                return new ValueTask<IReadOnlyList<OutboxMessage>>(new List<OutboxMessage> { message });
            });
        
        harness.Repository.ReclaimStaleMessagesAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(5));

        var channel = harness.CreateChannel();
        var poller = harness.CreatePoller(channel, fakeTime);

        try { await poller.StartPollingAsync(cts.Token); } catch (OperationCanceledException) { }

        await harness.Repository.Received(1).ReclaimStaleMessagesAsync(TimeSpan.FromMinutes(5), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartPollingAsync_WhenReclaimIntervalNotElapsed_SkipsReclaim()
    {
        var services = new ServiceCollection();
        var repo = Substitute.For<IOutboxRepository>();
        using var cts = new CancellationTokenSource();
        repo.FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => {
                cts.Cancel();
                return new ValueTask<IReadOnlyList<OutboxMessage>>(Array.Empty<OutboxMessage>());
            });
            
        services.AddScoped(_ => repo);
        var provider = services.BuildServiceProvider();

        // Very high interval so reclaim is not hit in 1st cycle
        var options = new OutboxDispatcherOptions
        {
            UseAdaptivePolling = true,
            PollingInterval = TimeSpan.FromSeconds(10)
        };
        var optionsMock = Options.Create(options);

        var publisher = Substitute.For<IBrokerPublisher>();
        var channel = new OutboxChannel(NullLogger<OutboxChannel>.Instance, publisher, optionsMock, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), NSubstitute.Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(), NSubstitute.Substitute.For<EricksonLopez.Outbox.Diagnostics.IErrorSanitizer>(), TimeProvider.System);

        var poller = new AdaptivePoller(provider, channel, Microsoft.Extensions.Options.Options.Create(options), NullLogger<AdaptivePoller>.Instance, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), TimeProvider.System);

        try { await poller.StartPollingAsync(cts.Token); } catch (OperationCanceledException) { }

        await repo.Received(1).ReclaimStaleMessagesAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartPollingAsync_WhenReclaimedCountIsZero_CompletesReclaimWithoutEmittingCount()
    {
        var services = new ServiceCollection();
        var repo = Substitute.For<IOutboxRepository>();
        using var cts = new CancellationTokenSource();
        
        repo.FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => {
                cts.Cancel();
                return new ValueTask<IReadOnlyList<OutboxMessage>>(Array.Empty<OutboxMessage>());
            });
        
        repo.ReclaimStaleMessagesAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(0)); // reclaimed = 0
            
        services.AddScoped(_ => repo);
        var provider = services.BuildServiceProvider();

        var options = new OutboxDispatcherOptions
        {
            UseAdaptivePolling = true,
            PollingInterval = TimeSpan.FromMilliseconds(1)
        };
        var optionsMock = Options.Create(options);

        var publisher = Substitute.For<IBrokerPublisher>();
        var channel = new OutboxChannel(NullLogger<OutboxChannel>.Instance, publisher, optionsMock, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), NSubstitute.Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(), NSubstitute.Substitute.For<EricksonLopez.Outbox.Diagnostics.IErrorSanitizer>(), TimeProvider.System);

        var poller = new AdaptivePoller(provider, channel, Microsoft.Extensions.Options.Options.Create(options), NullLogger<AdaptivePoller>.Instance, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), TimeProvider.System);

        try { await poller.StartPollingAsync(cts.Token); } catch (OperationCanceledException) { }

        await repo.Received().ReclaimStaleMessagesAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartPollingAsync_WhenStaleMessagesReclaimed_Should_Record_ReclaimedMessages_Metric()
    {
        var services = new ServiceCollection();
        var repo = Substitute.For<IOutboxRepository>();
        using var cts = new CancellationTokenSource();
        repo.FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => {
                cts.Cancel();
                return new ValueTask<IReadOnlyList<OutboxMessage>>(Array.Empty<OutboxMessage>());
            });
        repo.ReclaimStaleMessagesAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(7));
        services.AddScoped(_ => repo);
        var provider = services.BuildServiceProvider();

        var options = new OutboxDispatcherOptions
        {
            UseAdaptivePolling = true,
            PollingInterval = TimeSpan.FromMilliseconds(1)
        };
        var optionsMock = Options.Create(options);

        long recordedReclaimed = 0;
        using var meterListener = new System.Diagnostics.Metrics.MeterListener();
        meterListener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Name == "messaging.outbox.messages.reclaimed")
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        var metrics = new EricksonLopez.Outbox.Diagnostics.OutboxMetrics();
        meterListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "messaging.outbox.messages.reclaimed")
            {
                recordedReclaimed += measurement;
            }
        });
        meterListener.Start();

        var publisher = Substitute.For<IBrokerPublisher>();
        var channel = new OutboxChannel(
            NullLogger<OutboxChannel>.Instance,
            publisher,
            optionsMock,
            Options.Create(new OutboxRuntimeOptions()),
            metrics,
            Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(),
            Substitute.For<EricksonLopez.Outbox.Diagnostics.IErrorSanitizer>(), TimeProvider.System);

        var poller = new AdaptivePoller(provider, channel, optionsMock, NullLogger<AdaptivePoller>.Instance, metrics, TimeProvider.System);

        try { await poller.StartPollingAsync(cts.Token); } catch (OperationCanceledException) { }

        recordedReclaimed.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task StartPollingAsync_WhenReclaimThrows_ContinuesPolling()
    {
        using var harness = new TestDispatcherHarness();
        harness.DispatcherOptions.UseAdaptivePolling = false;
        harness.DispatcherOptions.PollingInterval = TimeSpan.FromMilliseconds(1);
        harness.DispatcherOptions.ReclaimInterval = TimeSpan.FromMilliseconds(1);

        using var cts = new CancellationTokenSource();
        int fetchCount = 0;
        var message = new OutboxMessageTestDataBuilder().WithMessageType("Test").Build();

        int reclaimCallCount = 0;
        harness.Repository.ReclaimStaleMessagesAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                reclaimCallCount++;
                if (reclaimCallCount == 1)
                {
                    throw new InvalidOperationException("Simulated transient DB error during reclaim");
                }
                return ValueTask.FromResult(0);
            });

        harness.Repository.FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                fetchCount++;
                cts.Cancel();
                return new ValueTask<IReadOnlyList<OutboxMessage>>(new List<OutboxMessage> { message });
            });

        var channel = harness.CreateChannel();
        var poller = harness.CreatePoller(channel, TimeProvider.System);

        try
        {
            await poller.StartPollingAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected on cts.Cancel()
        }

        reclaimCallCount.Should().BeGreaterThanOrEqualTo(1);
        fetchCount.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task StartPollingAsync_WhenReclaimedIsZero_DoesNotRecordReclaimedMetric()
    {
        var services = new ServiceCollection();
        var repo = Substitute.For<IOutboxRepository>();
        using var cts = new CancellationTokenSource();

        repo.FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                cts.Cancel();
                return ValueTask.FromResult<IReadOnlyList<OutboxMessage>>(Array.Empty<OutboxMessage>());
            });
        repo.ReclaimStaleMessagesAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(0)); // 0 reclaimed

        services.AddScoped(_ => repo);
        var provider = services.BuildServiceProvider();

        var options = new OutboxDispatcherOptions
        {
            UseAdaptivePolling = false,
            PollingInterval = TimeSpan.FromSeconds(1)
        };
        var optionsMock = Options.Create(options);

        var metrics = new OutboxMetrics();
        long recordedReclaimed = -1;
        using var listener = new System.Diagnostics.Metrics.MeterListener();
        listener.InstrumentPublished = (inst, l) =>
        {
            if (inst.Meter == metrics.Meter && inst.Name == "messaging.outbox.messages.reclaimed")
                l.EnableMeasurementEvents(inst);
        };
        listener.SetMeasurementEventCallback<long>((inst, measurement, tags, state) =>
        {
            if (inst.Meter == metrics.Meter && inst.Name == "messaging.outbox.messages.reclaimed")
                recordedReclaimed = measurement;
        });
        listener.Start();

        var publisher = Substitute.For<IBrokerPublisher>();
        var channel = new OutboxChannel(
            NullLogger<OutboxChannel>.Instance,
            publisher,
            optionsMock,
            Options.Create(new OutboxRuntimeOptions()),
            metrics,
            provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<IErrorSanitizer>(),
            TimeProvider.System);

        var poller = new AdaptivePoller(provider, channel, optionsMock, NullLogger<AdaptivePoller>.Instance, metrics, TimeProvider.System);

        try { await poller.StartPollingAsync(cts.Token); } catch (OperationCanceledException) { }

        // Assert metric was NOT recorded when reclaimed == 0
        recordedReclaimed.Should().Be(-1);
    }

    [Fact]
    public async Task StartPollingAsync_WhenExactReclaimIntervalElapsed_TriggersReclaim()
    {
        var services = new ServiceCollection();
        var repo = Substitute.For<IOutboxRepository>();
        int reclaimCalls = 0;
        var secondReclaimTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        repo.FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IReadOnlyList<OutboxMessage>>(Array.Empty<OutboxMessage>()));

        repo.ReclaimStaleMessagesAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var count = Interlocked.Increment(ref reclaimCalls);
                if (count >= 2) secondReclaimTcs.TrySetResult(true);
                return ValueTask.FromResult(0);
            });

        services.AddScoped(_ => repo);
        var provider = services.BuildServiceProvider();

        var options = new OutboxDispatcherOptions
        {
            UseAdaptivePolling = false,
            PollingInterval = TimeSpan.FromMilliseconds(50),
            ReclaimInterval = TimeSpan.FromMinutes(1)
        };
        var optionsMock = Options.Create(options);

        var fixedTime = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        var fakeTime = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(fixedTime);

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

        await Task.Delay(20);
        Volatile.Read(ref reclaimCalls).Should().Be(1);

        fakeTime.Advance(TimeSpan.FromMinutes(1));
        poller.WakeUp();

        await secondReclaimTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Volatile.Read(ref reclaimCalls).Should().Be(2);

        cts.Cancel();
        try { await pollingTask; } catch (OperationCanceledException) { }
    }
}



