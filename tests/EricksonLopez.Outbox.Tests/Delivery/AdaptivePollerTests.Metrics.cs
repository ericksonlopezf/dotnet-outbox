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
    public void Poller_OnObservableGaugeObservation_RecordsPendingMessagesGaugeMetric()
    {
        var provider = Substitute.For<IServiceProvider>();
        var optionsMock = Options.Create(new OutboxDispatcherOptions());
        var publisher = Substitute.For<IBrokerPublisher>();
        var channel = new OutboxChannel(NullLogger<OutboxChannel>.Instance, publisher, optionsMock, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), NSubstitute.Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(), NSubstitute.Substitute.For<EricksonLopez.Outbox.Diagnostics.IErrorSanitizer>(), TimeProvider.System);

        System.Diagnostics.Metrics.Instrument? capturedInstrument = null;
        using var listener = new System.Diagnostics.Metrics.MeterListener();
        listener.InstrumentPublished = (instrument, l) =>
        {
            if (instrument.Name == "messaging.outbox.pending.messages")
            {
                capturedInstrument = instrument;
                l.EnableMeasurementEvents(instrument);
            }
        };

        long recordedValue = -1;
        listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            if (instrument.Name == "messaging.outbox.pending.messages")
            {
                recordedValue = measurement;
            }
        });

        listener.Start();

        var poller = new AdaptivePoller(provider, channel, optionsMock, NullLogger<AdaptivePoller>.Instance, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), TimeProvider.System);

        // Force the ObservableGauge to be evaluated
        listener.RecordObservableInstruments();

        recordedValue.Should().Be(0);
        capturedInstrument.Should().NotBeNull();
        capturedInstrument!.Unit.Should().Be("{message}");
        capturedInstrument.Description.Should().Be("Current approximate number of pending outbox messages.");
    }

    [Fact]
    public async Task Poller_Should_Collect_Metrics_When_Interval_Elapsed()
    {
        var services = new ServiceCollection();
        var repo = Substitute.For<IOutboxRepository>();
        using var cts = new CancellationTokenSource();
        
        repo.FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => {
                cts.Cancel();
                return new ValueTask<IReadOnlyList<OutboxMessage>>(Array.Empty<OutboxMessage>());
            });
        repo.GetPendingCountAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(42L));
            
        services.AddScoped(_ => repo);
        var provider = services.BuildServiceProvider();

        var options = new OutboxDispatcherOptions
        {
            UseAdaptivePolling = false,
            PendingCountRefreshInterval = TimeSpan.FromSeconds(30),
            PollingInterval = TimeSpan.FromSeconds(5)
        };
        var optionsMock = Options.Create(options);

        var publisher = Substitute.For<IBrokerPublisher>();
        var channel = new OutboxChannel(NullLogger<OutboxChannel>.Instance, publisher, optionsMock, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), NSubstitute.Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(), NSubstitute.Substitute.For<EricksonLopez.Outbox.Diagnostics.IErrorSanitizer>(), TimeProvider.System);

        var poller = new AdaptivePoller(provider, channel, Microsoft.Extensions.Options.Options.Create(options), NullLogger<AdaptivePoller>.Instance, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), TimeProvider.System);

        // White-box test rationale: directly simulates time elapsed on the internal timestamp field
        ReflectionTestHelper.SetFieldValue(poller, "_lastMetricTick", TimeProvider.System.GetTimestamp() - (long)(60000.0 * TimeProvider.System.TimestampFrequency / 1000.0));

        try { await poller.StartPollingAsync(cts.Token); } catch (OperationCanceledException) { }

        // The metric interval logic should have executed
        await repo.Received(1).GetPendingCountAsync(Arg.Any<CancellationToken>());
        
        // Assert the gauge metric was updated
        long pendingCount = ReflectionTestHelper.GetFieldValue<long>(poller, "_pendingCount");
        pendingCount.Should().Be(42);
    }

    [Fact]
    public async Task Poller_Should_Not_Collect_Metrics_If_Interval_Not_Elapsed()
    {
        var services = new ServiceCollection();
        var repo = Substitute.For<IOutboxRepository>();
        using var cts2 = new CancellationTokenSource();
        
        repo.FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => {
                cts2.Cancel();
                return new ValueTask<IReadOnlyList<OutboxMessage>>(Array.Empty<OutboxMessage>());
            });
            
        services.AddScoped(_ => repo);
        var provider = services.BuildServiceProvider();

        var options = new OutboxDispatcherOptions
        {
            UseAdaptivePolling = false,
            PollingInterval = TimeSpan.FromSeconds(5)
        };
        var optionsMock = Options.Create(options);

        var publisher = Substitute.For<IBrokerPublisher>();
        var channel = new OutboxChannel(NullLogger<OutboxChannel>.Instance, publisher, optionsMock, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), NSubstitute.Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(), NSubstitute.Substitute.For<EricksonLopez.Outbox.Diagnostics.IErrorSanitizer>(), TimeProvider.System);

        var poller = new AdaptivePoller(provider, channel, Microsoft.Extensions.Options.Options.Create(options), NullLogger<AdaptivePoller>.Instance, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), TimeProvider.System);

        // White-box test rationale: simulates time not yet elapsed on internal timestamp field
        ReflectionTestHelper.SetFieldValue(poller, "_lastMetricTick", TimeProvider.System.GetTimestamp() - (long)(10000.0 * TimeProvider.System.TimestampFrequency / 1000.0)); // 10000 < 30000

        try { await poller.StartPollingAsync(cts2.Token); } catch (OperationCanceledException) { }

        // The metric interval logic should NOT have executed
        await repo.DidNotReceive().GetPendingCountAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartPollingAsync_WhenMessagesFetched_RecordsBatchSizeMetric()
    {
        var services = new ServiceCollection();
        var repo = Substitute.For<IOutboxRepository>();
        var message = new OutboxMessage(Guid.NewGuid(), "Test", default, null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        repo.FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<OutboxMessage>>(new List<OutboxMessage> { message }));
        services.AddScoped(_ => repo);
        var provider = services.BuildServiceProvider();

        var options = new OutboxDispatcherOptions
        {
            UseAdaptivePolling = false,
            BatchSize = 10,
            PollingInterval = TimeSpan.FromMilliseconds(20)
        };
        var optionsMock = Options.Create(options);

        var metrics = new EricksonLopez.Outbox.Diagnostics.OutboxMetrics();
        int recordedBatchSize = 0;
        var batchMetricTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var meterListener = new System.Diagnostics.Metrics.MeterListener();
        meterListener.InstrumentPublished = (inst, listener) =>
        {
            if (inst.Meter.Name == "EricksonLopez.Outbox" && inst.Name == "messaging.outbox.poller.batch_size")
            {
                listener.EnableMeasurementEvents(inst);
            }
        };
        meterListener.SetMeasurementEventCallback<int>((inst, measurement, tags, state) =>
        {
            recordedBatchSize = measurement;
            batchMetricTcs.TrySetResult();
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

        using var cts = new CancellationTokenSource();
        var pollingTask = poller.StartPollingAsync(cts.Token);
        await batchMetricTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();
        try { await pollingTask; } catch (OperationCanceledException) { }

        recordedBatchSize.Should().Be(1);
    }

    [Fact]
    public async Task StartPollingAsync_WhenIntervalElapsed_UpdatesPendingCountGauge()
    {
        var services = new ServiceCollection();
        var repo = Substitute.For<IOutboxRepository>();
        repo.FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<OutboxMessage>>(Array.Empty<OutboxMessage>()));
        var pendingCountTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        repo.GetPendingCountAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                pendingCountTcs.TrySetResult();
                return ValueTask.FromResult(42L);
            });
        services.AddScoped(_ => repo);
        var provider = services.BuildServiceProvider();

        var options = new OutboxDispatcherOptions
        {
            UseAdaptivePolling = false,
            PendingCountRefreshInterval = TimeSpan.FromMilliseconds(1),
            PollingInterval = TimeSpan.FromMilliseconds(10)
        };
        var optionsMock = Options.Create(options);

        var metrics = new EricksonLopez.Outbox.Diagnostics.OutboxMetrics();
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

        using var cts = new CancellationTokenSource();
        var pollingTask = poller.StartPollingAsync(cts.Token);
        await pendingCountTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();
        try { await pollingTask; } catch (OperationCanceledException) { }

        await repo.Received().GetPendingCountAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartPollingAsync_WhenIntervalNotElapsed_DoesNotUpdatePendingCount()
    {
        var services = new ServiceCollection();
        var repo = Substitute.For<IOutboxRepository>();
        var fetchTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        repo.FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                fetchTcs.TrySetResult();
                return ValueTask.FromResult<IReadOnlyList<OutboxMessage>>(Array.Empty<OutboxMessage>());
            });
        services.AddScoped(_ => repo);
        var provider = services.BuildServiceProvider();

        var options = new OutboxDispatcherOptions
        {
            UseAdaptivePolling = false,
            PendingCountRefreshInterval = TimeSpan.FromHours(1),
            PollingInterval = TimeSpan.FromMilliseconds(10)
        };
        var optionsMock = Options.Create(options);

        var metrics = new EricksonLopez.Outbox.Diagnostics.OutboxMetrics();
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

        // White-box test rationale: simulates that metric tick has just occurred so refresh interval is not elapsed
        ReflectionTestHelper.SetFieldValue(poller, "_lastMetricTick", TimeProvider.System.GetTimestamp());

        using var cts = new CancellationTokenSource();
        var pollingTask = poller.StartPollingAsync(cts.Token);
        await fetchTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();
        try { await pollingTask; } catch (OperationCanceledException) { }

        await repo.DidNotReceive().GetPendingCountAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartPollingAsync_WhenNoMessagesFetched_DoesNotRecordBatchSizeMetric()
    {
        var services = new ServiceCollection();
        var repo = Substitute.For<IOutboxRepository>();
        var fetchTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        repo.FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                fetchTcs.TrySetResult();
                return ValueTask.FromResult<IReadOnlyList<OutboxMessage>>(Array.Empty<OutboxMessage>());
            });
        services.AddScoped(_ => repo);
        var provider = services.BuildServiceProvider();

        var options = new OutboxDispatcherOptions
        {
            UseAdaptivePolling = false,
            BatchSize = 10,
            PollingInterval = TimeSpan.FromMilliseconds(20)
        };
        var optionsMock = Options.Create(options);

        var metrics = new EricksonLopez.Outbox.Diagnostics.OutboxMetrics();
        int recordCount = 0;
        using var meterListener = new System.Diagnostics.Metrics.MeterListener();
        meterListener.InstrumentPublished = (inst, listener) =>
        {
            if (inst.Meter == metrics.Meter && inst.Name == "messaging.outbox.poller.batch_size")
            {
                listener.EnableMeasurementEvents(inst);
            }
        };
        meterListener.SetMeasurementEventCallback<int>((inst, measurement, tags, state) =>
        {
            recordCount++;
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

        using var cts = new CancellationTokenSource();
        var pollingTask = poller.StartPollingAsync(cts.Token);
        await fetchTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cts.Cancel();
        try { await pollingTask; } catch (OperationCanceledException) { }

        recordCount.Should().Be(0);
    }

    [Fact]
    public async Task UpdatePendingCountAsync_RespectsInterval_DoesNotCallRepoEveryPoll()
    {
        var services = new ServiceCollection();
        var repo = Substitute.For<IOutboxRepository>();
        int getPendingCalls = 0;
        int fetchCalls = 0;
        repo.GetPendingCountAsync(Arg.Any<CancellationToken>())
            .Returns(_ => {
                Interlocked.Increment(ref getPendingCalls);
                return ValueTask.FromResult(42L);
            });
        repo.FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => {
                Interlocked.Increment(ref fetchCalls);
                return ValueTask.FromResult<IReadOnlyList<OutboxMessage>>(Array.Empty<OutboxMessage>());
            });
        services.AddScoped(_ => repo);
        var provider = services.BuildServiceProvider();

        var options = new OutboxDispatcherOptions
        {
            UseAdaptivePolling = false,
            BatchSize = 10,
            PollingInterval = TimeSpan.FromMilliseconds(10)
        };
        var optionsMock = Options.Create(options);

        var publisher = Substitute.For<IBrokerPublisher>();
        var channel = new OutboxChannel(
            NullLogger<OutboxChannel>.Instance,
            publisher,
            optionsMock,
            Options.Create(new OutboxRuntimeOptions()),
            new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<EricksonLopez.Outbox.Diagnostics.IErrorSanitizer>(), TimeProvider.System);

        var fakeTime = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var poller = new AdaptivePoller(provider, channel, optionsMock, NullLogger<AdaptivePoller>.Instance, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), fakeTime);

        using var cts = new CancellationTokenSource();
        var task = poller.StartPollingAsync(cts.Token);

        // Advance through multiple poll cycles, which remain under the default 5000ms metric interval
        for (int i = 0; i < 3; i++)
        {
            fakeTime.Advance(TimeSpan.FromMilliseconds(20));
            await Task.Yield();
        }

        cts.Cancel();
        try { await task; } catch (OperationCanceledException) { }

        // Initial call happened at start, but subsequent polls within interval did not re-fetch pending count
        Volatile.Read(ref getPendingCalls).Should().Be(1);
    }

    [Fact]
    public async Task StartPollingAsync_OnFirstPoll_ImmediatelyUpdatesPendingCount()
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
        repo.GetPendingCountAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(99L));

        services.AddScoped(_ => repo);
        var provider = services.BuildServiceProvider();

        var options = new OutboxDispatcherOptions
        {
            UseAdaptivePolling = false,
            PendingCountRefreshInterval = TimeSpan.FromSeconds(30),
            PollingInterval = TimeSpan.FromSeconds(5)
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

        try { await poller.StartPollingAsync(cts.Token); } catch (OperationCanceledException) { }

        await repo.Received(1).GetPendingCountAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartPollingAsync_WhenPendingCountIntervalNotStrictlyElapsed_DoesNotUpdateAgain()
    {
        var services = new ServiceCollection();
        var repo = Substitute.For<IOutboxRepository>();
        int pendingCountCalls = 0;

        repo.FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IReadOnlyList<OutboxMessage>>(Array.Empty<OutboxMessage>()));
        repo.GetPendingCountAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref pendingCountCalls);
                return ValueTask.FromResult(10L);
            });

        services.AddScoped(_ => repo);
        var provider = services.BuildServiceProvider();

        var options = new OutboxDispatcherOptions
        {
            UseAdaptivePolling = false,
            PendingCountRefreshInterval = TimeSpan.FromSeconds(30),
            PollingInterval = TimeSpan.FromMilliseconds(50)
        };
        var optionsMock = Options.Create(options);

        var fakeTime = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
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
        Volatile.Read(ref pendingCountCalls).Should().Be(1);

        fakeTime.Advance(TimeSpan.FromSeconds(30));
        poller.WakeUp();
        await Task.Delay(20);
        Volatile.Read(ref pendingCountCalls).Should().Be(1);

        fakeTime.Advance(TimeSpan.FromMilliseconds(1));
        poller.WakeUp();
        await Task.Delay(50);
        Volatile.Read(ref pendingCountCalls).Should().Be(2);

        cts.Cancel();
        try { await pollingTask; } catch (OperationCanceledException) { }
    }
}



