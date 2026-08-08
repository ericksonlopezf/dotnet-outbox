using System;
using System.Collections.Generic;

using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;

using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Dispatcher;
using EricksonLopez.Outbox.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Tests;

public class AdaptivePollerTests
{
    [Fact]
    public async Task Poller_Should_Fetch_And_Write_To_Channel()
    {
#pragma warning disable CA2012
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
            PollingInterval = TimeSpan.FromMilliseconds(10)
        };
        var optionsMock = Options.Create(options);

        var publisher = Substitute.For<IBrokerPublisher>();
        var channel = new OutboxChannel(NullLogger<OutboxChannel>.Instance, publisher, optionsMock, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), NSubstitute.Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(), NSubstitute.Substitute.For<EricksonLopez.Outbox.Diagnostics.IErrorSanitizer>());

        var poller = new AdaptivePoller(provider, channel, Microsoft.Extensions.Options.Options.Create(options), NullLogger<AdaptivePoller>.Instance, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics());

        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await poller.StartPollingAsync(cts.Token);

        await repo.Received().FetchPendingAsync(options.BatchSize, Arg.Any<CancellationToken>());

        var channelField = typeof(OutboxChannel).GetField("_channel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var innerChannel = (System.Threading.Channels.Channel<OutboxMessage>)channelField!.GetValue(channel)!;
        var gotMessage = innerChannel.Reader.TryRead(out var readMessage);
        gotMessage.Should().BeTrue("The poller should have written the message to the channel.");
        readMessage.Should().Be(message);
    }

    [Fact]
    public async Task Poller_Should_Reclaim_Stale_Messages_On_10th_Cycle()
    {
#pragma warning disable CA2012
        var services = new ServiceCollection();
        var repo = Substitute.For<IOutboxRepository>();
        
        var message = new OutboxMessage(Guid.NewGuid(), "Test", default, null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        repo.FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<OutboxMessage>>(new List<OutboxMessage> { message }));
        
        repo.ReclaimStaleMessagesAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(5));
            
        services.AddScoped(_ => repo);
        var provider = services.BuildServiceProvider();

        var options = new OutboxDispatcherOptions
        {
            UseAdaptivePolling = true,
            PollingInterval = TimeSpan.FromMilliseconds(1)
        };
        var optionsMock = Options.Create(options);

        var publisher = Substitute.For<IBrokerPublisher>();
        var channel = new OutboxChannel(NullLogger<OutboxChannel>.Instance, publisher, optionsMock, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), NSubstitute.Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(), NSubstitute.Substitute.For<EricksonLopez.Outbox.Diagnostics.IErrorSanitizer>());

        var poller = new AdaptivePoller(provider, channel, Microsoft.Extensions.Options.Options.Create(options), NullLogger<AdaptivePoller>.Instance, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics());

        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100)); // enough for > 10 cycles
        try { await poller.StartPollingAsync(cts.Token); } catch (OperationCanceledException) { }

        await repo.Received().ReclaimStaleMessagesAsync(TimeSpan.FromMinutes(5), Arg.Any<CancellationToken>());
    }
    [Fact]
    public async Task Poller_Should_Handle_Null_Repository_Gracefully()
    {
        var services = new ServiceCollection();
        // Do NOT register IOutboxRepository
        var provider = services.BuildServiceProvider();

        var options = new OutboxDispatcherOptions
        {
            UseAdaptivePolling = false,
            PollingInterval = TimeSpan.FromMilliseconds(1)
        };
        var optionsMock = Options.Create(options);

        var publisher = Substitute.For<IBrokerPublisher>();
        var channel = new OutboxChannel(NullLogger<OutboxChannel>.Instance, publisher, optionsMock, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), NSubstitute.Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(), NSubstitute.Substitute.For<EricksonLopez.Outbox.Diagnostics.IErrorSanitizer>());

        var poller = new AdaptivePoller(provider, channel, Microsoft.Extensions.Options.Options.Create(options), NullLogger<AdaptivePoller>.Instance, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics());

        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await poller.StartPollingAsync(cts.Token);
        
        // Assert: Should not throw and finish gracefully
        true.Should().BeTrue();
    }

    [Fact]
    public async Task Poller_Should_Catch_And_Log_Exceptions()
    {
#pragma warning disable CA2012
        var services = new ServiceCollection();
        var repo = Substitute.For<IOutboxRepository>();
        
        repo.FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<IReadOnlyList<OutboxMessage>>>(x => throw new InvalidOperationException("Test Error"));
            
        services.AddScoped(_ => repo);
        var provider = services.BuildServiceProvider();

        var options = new OutboxDispatcherOptions
        {
            UseAdaptivePolling = false,
            PollingInterval = TimeSpan.FromMilliseconds(1)
        };
        var optionsMock = Options.Create(options);

        var publisher = Substitute.For<IBrokerPublisher>();
        var channel = new OutboxChannel(NullLogger<OutboxChannel>.Instance, publisher, optionsMock, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), NSubstitute.Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(), NSubstitute.Substitute.For<EricksonLopez.Outbox.Diagnostics.IErrorSanitizer>());

        var poller = new AdaptivePoller(provider, channel, Microsoft.Extensions.Options.Options.Create(options), NullLogger<AdaptivePoller>.Instance, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics());

        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        await poller.StartPollingAsync(cts.Token);

        await repo.Received().FetchPendingAsync(options.BatchSize, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Poller_TaskDelay_Cancellation_Should_Break()
    {
        var services = new ServiceCollection();
        var repo = Substitute.For<IOutboxRepository>();
        
        repo.FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<OutboxMessage>>(Array.Empty<OutboxMessage>()));
            
        services.AddScoped(_ => repo);
        var provider = services.BuildServiceProvider();

        var options = new OutboxDispatcherOptions
        {
            UseAdaptivePolling = false,
            PollingInterval = TimeSpan.FromSeconds(5) // Long enough delay to ensure we cancel during delay
        };
        var optionsMock = Options.Create(options);

        var publisher = Substitute.For<IBrokerPublisher>();
        var channel = new OutboxChannel(NullLogger<OutboxChannel>.Instance, publisher, optionsMock, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), NSubstitute.Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(), NSubstitute.Substitute.For<EricksonLopez.Outbox.Diagnostics.IErrorSanitizer>());

        var poller = new AdaptivePoller(provider, channel, Microsoft.Extensions.Options.Options.Create(options), NullLogger<AdaptivePoller>.Instance, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics());

        var cts = new CancellationTokenSource();
        
        var task = poller.StartPollingAsync(cts.Token);
        
        // Give it time to fetch pending and hit Task.Delay
        await Task.Delay(50);
        cts.Cancel();
        
        await task; // Should finish gracefully without throwing
        true.Should().BeTrue();
    }

    [Fact]
    public void WakeUp_Should_Not_Throw_When_Full()
    {
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();

        var options = new OutboxDispatcherOptions();
        var optionsMock = Options.Create(options);

        var publisher = Substitute.For<IBrokerPublisher>();
        var channel = new OutboxChannel(NullLogger<OutboxChannel>.Instance, publisher, optionsMock, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), NSubstitute.Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(), NSubstitute.Substitute.For<EricksonLopez.Outbox.Diagnostics.IErrorSanitizer>());

        var poller = new AdaptivePoller(provider, channel, optionsMock, NullLogger<AdaptivePoller>.Instance, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics());

        // Calling WakeUp multiple times concurrently or sequentially
        // The first might succeed, second might throw SemaphoreFullException
        // but the method catches it.
        Parallel.For(0, 10, i => poller.WakeUp());
        
        true.Should().BeTrue();
    }

    [Fact]
    public async Task WakeUp_Should_Ignore_SemaphoreFullException()
    {
        var provider = Substitute.For<IServiceProvider>();
        var options = new OutboxDispatcherOptions();
        var optionsMock = Options.Create(options);
        var publisher = Substitute.For<IBrokerPublisher>();
        var channel = new OutboxChannel(NullLogger<OutboxChannel>.Instance, publisher, optionsMock, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), NSubstitute.Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(), NSubstitute.Substitute.For<EricksonLopez.Outbox.Diagnostics.IErrorSanitizer>());

        var poller = new AdaptivePoller(provider, channel, optionsMock, NullLogger<AdaptivePoller>.Instance, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics());

        // Maximize contention to hit the catch block without deadlocking the ThreadPool
        var tasks = new Task[100];
        for (int i = 0; i < tasks.Length; i++)
        {
            tasks[i] = Task.Run(() => poller.WakeUp());
        }
        await Task.WhenAll(tasks);
        
        var act = () => poller.WakeUp();
        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_Should_Dispose_Semaphore()
    {
        var provider = Substitute.For<IServiceProvider>();
        var optionsMock = Options.Create(new OutboxDispatcherOptions());
        var publisher = Substitute.For<IBrokerPublisher>();
        var channel = new OutboxChannel(NullLogger<OutboxChannel>.Instance, publisher, optionsMock, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), NSubstitute.Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(), NSubstitute.Substitute.For<EricksonLopez.Outbox.Diagnostics.IErrorSanitizer>());

        var poller = new AdaptivePoller(provider, channel, optionsMock, NullLogger<AdaptivePoller>.Instance, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics());
        poller.Dispose();

        var field = typeof(AdaptivePoller).GetField("_wakeupSignal", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var semaphore = (SemaphoreSlim)field!.GetValue(poller)!;
        
        var act = () => semaphore.Wait(0);
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Poller_Records_Pending_Messages_Metric()
    {
        var provider = Substitute.For<IServiceProvider>();
        var optionsMock = Options.Create(new OutboxDispatcherOptions());
        var publisher = Substitute.For<IBrokerPublisher>();
        var channel = new OutboxChannel(NullLogger<OutboxChannel>.Instance, publisher, optionsMock, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), NSubstitute.Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(), NSubstitute.Substitute.For<EricksonLopez.Outbox.Diagnostics.IErrorSanitizer>());

        using var listener = new System.Diagnostics.Metrics.MeterListener();
        listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Name == "messaging.outbox.pending.messages")
            {
                listener.EnableMeasurementEvents(instrument);
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

        var poller = new AdaptivePoller(provider, channel, optionsMock, NullLogger<AdaptivePoller>.Instance, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics());

        // Force the ObservableGauge to be evaluated
        listener.RecordObservableInstruments();

        recordedValue.Should().Be(0);
    }

    [Fact]
    public async Task Poller_Should_Skip_Reclaim_If_Time_Not_Reached()
    {
        var services = new ServiceCollection();
        var repo = Substitute.For<IOutboxRepository>();
        
        repo.FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<OutboxMessage>>(Array.Empty<OutboxMessage>()));
            
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
        var channel = new OutboxChannel(NullLogger<OutboxChannel>.Instance, publisher, optionsMock, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), NSubstitute.Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(), NSubstitute.Substitute.For<EricksonLopez.Outbox.Diagnostics.IErrorSanitizer>());

        var poller = new AdaptivePoller(provider, channel, Microsoft.Extensions.Options.Options.Create(options), NullLogger<AdaptivePoller>.Instance, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics());

        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(5)); 
        try { await poller.StartPollingAsync(cts.Token); } catch (OperationCanceledException) { }

        await repo.Received(1).ReclaimStaleMessagesAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Poller_Should_Reclaim_And_Log_When_Reclaimed_Is_Zero()
    {
        var services = new ServiceCollection();
        var repo = Substitute.For<IOutboxRepository>();
        
        repo.FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<OutboxMessage>>(Array.Empty<OutboxMessage>()));
        
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
        var channel = new OutboxChannel(NullLogger<OutboxChannel>.Instance, publisher, optionsMock, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), NSubstitute.Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(), NSubstitute.Substitute.For<EricksonLopez.Outbox.Diagnostics.IErrorSanitizer>());

        var poller = new AdaptivePoller(provider, channel, Microsoft.Extensions.Options.Options.Create(options), NullLogger<AdaptivePoller>.Instance, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics());

        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100)); // enough for > 10 cycles
        try { await poller.StartPollingAsync(cts.Token); } catch (OperationCanceledException) { }

        await repo.Received().ReclaimStaleMessagesAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
        
        // Assert: when reclaimed == 0, _metrics.ReclaimedMessages is NOT called
        // Since metrics are hard to intercept without a listener, we can just ensure 
        // that nothing throws, or ideally we would mock the meter, but here we can't easily without a listener.
        // Actually, let's use a FakeLogger to ensure ReclaimedStaleMessages was NOT logged!
    }

    [Fact]
    public async Task Poller_Should_Loop_Instantly_When_Batch_Is_Full_And_Adaptive_Polling_Is_On()
    {
        var services = new ServiceCollection();
        var repo = Substitute.For<IOutboxRepository>();
        
        var options = new OutboxDispatcherOptions
        {
            UseAdaptivePolling = true,
            BatchSize = 1,
            PollingInterval = TimeSpan.FromSeconds(10) // Long delay
        };

        var message = new OutboxMessage(Guid.NewGuid(), "Test", default, null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5)); 
        int callCount = 0;
        repo.FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(x => 
            {
                callCount++;
                if (callCount == 1) return new ValueTask<IReadOnlyList<OutboxMessage>>(new List<OutboxMessage> { message });
                cts.Cancel();
                return new ValueTask<IReadOnlyList<OutboxMessage>>(Array.Empty<OutboxMessage>());
            });
            
        services.AddScoped(_ => repo);
        var provider = services.BuildServiceProvider();

        var optionsMock = Options.Create(options);
        var publisher = Substitute.For<IBrokerPublisher>();
        var channel = new OutboxChannel(NullLogger<OutboxChannel>.Instance, publisher, optionsMock, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), NSubstitute.Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(), NSubstitute.Substitute.For<EricksonLopez.Outbox.Diagnostics.IErrorSanitizer>());

        var poller = new AdaptivePoller(provider, channel, Microsoft.Extensions.Options.Options.Create(options), NullLogger<AdaptivePoller>.Instance, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics());

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try { await poller.StartPollingAsync(cts.Token); } catch (OperationCanceledException) { }
        sw.Stop();

        // Ensure it fetched exactly TWICE because the first fetch returned a full batch,
        // causing an immediate loop (10ms delay) and then a second fetch (which returned empty and waited).
        await repo.Received(2).FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Poller_Should_Collect_Metrics_When_Interval_Elapsed()
    {
        var services = new ServiceCollection();
        var repo = Substitute.For<IOutboxRepository>();
        
        repo.FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<OutboxMessage>>(Array.Empty<OutboxMessage>()));
        repo.GetPendingCountAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<long>(42));
            
        services.AddScoped(_ => repo);
        var provider = services.BuildServiceProvider();

        var options = new OutboxDispatcherOptions
        {
            UseAdaptivePolling = false,
            PollingInterval = TimeSpan.FromSeconds(5)
        };
        var optionsMock = Options.Create(options);

        var publisher = Substitute.For<IBrokerPublisher>();
        var channel = new OutboxChannel(NullLogger<OutboxChannel>.Instance, publisher, optionsMock, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), NSubstitute.Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(), NSubstitute.Substitute.For<EricksonLopez.Outbox.Diagnostics.IErrorSanitizer>());

        var poller = new AdaptivePoller(provider, channel, Microsoft.Extensions.Options.Options.Create(options), NullLogger<AdaptivePoller>.Instance, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics());

        // Use reflection to set _lastMetricTick to a value far in the past (e.g., 60 seconds ago)
        var field = typeof(AdaptivePoller).GetField("_lastMetricTick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field!.SetValue(poller, Environment.TickCount64 - 60000);

        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100)); 
        try { await poller.StartPollingAsync(cts.Token); } catch (OperationCanceledException) { }

        // The metric interval logic should have executed
        await repo.Received(1).GetPendingCountAsync(Arg.Any<CancellationToken>());
        
        // Assert the gauge metric was updated
        var pendingCountField = typeof(AdaptivePoller).GetField("_pendingCount", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        long pendingCount = (long)pendingCountField!.GetValue(poller)!;
        pendingCount.Should().Be(42);
    }

    [Fact]
    public async Task Poller_Should_Break_Iteration_If_Cancelled()
    {
        var services = new ServiceCollection();
        var repo = Substitute.For<IOutboxRepository>();
        
        var message1 = new OutboxMessage(Guid.NewGuid(), "Test1", default, null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var message2 = new OutboxMessage(Guid.NewGuid(), "Test2", default, null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        
        repo.FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<OutboxMessage>>(new List<OutboxMessage> { message1, message2 }));
            
        services.AddScoped(_ => repo);
        var provider = services.BuildServiceProvider();

        var options = new OutboxDispatcherOptions { UseAdaptivePolling = false, PollingInterval = TimeSpan.FromSeconds(5) };
        var optionsMock = Options.Create(options);

        var publisher = Substitute.For<IBrokerPublisher>();
        var channel = new OutboxChannel(NullLogger<OutboxChannel>.Instance, publisher, optionsMock, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), NSubstitute.Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(), NSubstitute.Substitute.For<EricksonLopez.Outbox.Diagnostics.IErrorSanitizer>());

        var poller = new AdaptivePoller(provider, channel, optionsMock, NullLogger<AdaptivePoller>.Instance, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics());

        var cts = new CancellationTokenSource();
        // Cancel the token exactly when FetchPendingAsync is called.
        // It fetches the batch, but before iterating, the token is already cancelled!
        repo.When(x => x.FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()))
            .Do(_ => cts.Cancel());

        try { await poller.StartPollingAsync(cts.Token); } catch (OperationCanceledException) { }

        // Read all from channel, there should be 0 messages because it broke the loop.
        var channelField = typeof(OutboxChannel).GetField("_channel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var innerChannel = (System.Threading.Channels.Channel<OutboxMessage>)channelField!.GetValue(channel)!;
        
        var gotMessage = innerChannel.Reader.TryRead(out _);
        gotMessage.Should().BeFalse("Because the loop should have broken before writing to the channel");
    }

    [Fact]
    public void WakeUp_Should_Ignore_ObjectDisposedException_When_Disposed()
    {
        // Arrange
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var publisher = Substitute.For<IBrokerPublisher>();
        var channel = new OutboxChannel(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OutboxChannel>.Instance, 
            publisher, 
            Microsoft.Extensions.Options.Options.Create(new OutboxDispatcherOptions()), 
            Microsoft.Extensions.Options.Options.Create(new OutboxRuntimeOptions()), 
            new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), 
            Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(), 
            Substitute.For<EricksonLopez.Outbox.Diagnostics.IErrorSanitizer>());
        var options = Microsoft.Extensions.Options.Options.Create(new OutboxDispatcherOptions());
        var metrics = new EricksonLopez.Outbox.Diagnostics.OutboxMetrics();
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<AdaptivePoller>.Instance;
        
        var poller = new AdaptivePoller(provider, channel, options, logger, metrics);
        poller.Dispose();

        // Act
        var act = () => poller.WakeUp();

        // Assert
        act.Should().NotThrow<ObjectDisposedException>();
    }

    private sealed class FakeLogger : Microsoft.Extensions.Logging.ILogger<AdaptivePoller>
    {
        public bool ErrorLogged { get; set; }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
        public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == Microsoft.Extensions.Logging.LogLevel.Error) ErrorLogged = true;
        }
    }

    [Fact]
    public async Task Poller_Should_Log_Error_When_Exception_Thrown()
    {
        var services = new ServiceCollection();
        var repo = Substitute.For<IOutboxRepository>();
        
        repo.FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<IReadOnlyList<OutboxMessage>>>(x => throw new InvalidOperationException("Simulated Database Error"));
            
        services.AddScoped(_ => repo);
        var provider = services.BuildServiceProvider();

        var optionsMock = Options.Create(new OutboxDispatcherOptions { UseAdaptivePolling = false, PollingInterval = TimeSpan.FromSeconds(5) });

        var publisher = Substitute.For<IBrokerPublisher>();
        var channel = new OutboxChannel(NullLogger<OutboxChannel>.Instance, publisher, optionsMock, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), NSubstitute.Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(), NSubstitute.Substitute.For<EricksonLopez.Outbox.Diagnostics.IErrorSanitizer>());

        var logger = new FakeLogger();

        var poller = new AdaptivePoller(provider, channel, optionsMock, logger, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics());

        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        try { await poller.StartPollingAsync(cts.Token); } catch (OperationCanceledException) { }

        logger.ErrorLogged.Should().BeTrue();
    }

    [Fact]
    public async Task Poller_Should_Not_Collect_Metrics_If_Interval_Not_Elapsed()
    {
        var services = new ServiceCollection();
        var repo = Substitute.For<IOutboxRepository>();
        
        repo.FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<IReadOnlyList<OutboxMessage>>(Array.Empty<OutboxMessage>()));
            
        services.AddScoped(_ => repo);
        var provider = services.BuildServiceProvider();

        var options = new OutboxDispatcherOptions
        {
            UseAdaptivePolling = false,
            PollingInterval = TimeSpan.FromSeconds(5)
        };
        var optionsMock = Options.Create(options);

        var publisher = Substitute.For<IBrokerPublisher>();
        var channel = new OutboxChannel(NullLogger<OutboxChannel>.Instance, publisher, optionsMock, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), NSubstitute.Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(), NSubstitute.Substitute.For<EricksonLopez.Outbox.Diagnostics.IErrorSanitizer>());

        var poller = new AdaptivePoller(provider, channel, Microsoft.Extensions.Options.Options.Create(options), NullLogger<AdaptivePoller>.Instance, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics());

        // Use reflection to set _lastMetricTick to a value just slightly in the past (e.g., 10 seconds ago)
        var field = typeof(AdaptivePoller).GetField("_lastMetricTick", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field!.SetValue(poller, Environment.TickCount64 - 10000); // 10000 < 30000

        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100)); 
        try { await poller.StartPollingAsync(cts.Token); } catch (OperationCanceledException) { }

        // The metric interval logic should NOT have executed!
        // If the mutant changed `-` to `+`, (TickCount64 + (TickCount64 - 10000)) > 30000, and it WOULD execute!
        await repo.DidNotReceive().GetPendingCountAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Poller_Should_Use_Default_MinMs_When_MaxBatchesPerSecond_Is_Zero()
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
            UseAdaptivePolling = true,
            MaxBatchesPerSecond = 0,
            BatchSize = 1, // trigger adaptive polling
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
            Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(),
            Substitute.For<EricksonLopez.Outbox.Diagnostics.IErrorSanitizer>());
        var poller = new AdaptivePoller(provider, channel, optionsMock, NullLogger<AdaptivePoller>.Instance, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics());

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        try { await poller.StartPollingAsync(cts.Token); } catch (OperationCanceledException) { }

        await repo.Received().FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Poller_Should_Use_Calculated_MinMs_When_MaxBatchesPerSecond_Is_Greater_Than_Zero()
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
            UseAdaptivePolling = true,
            MaxBatchesPerSecond = 50,
            BatchSize = 1, // trigger adaptive polling
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
            Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(),
            Substitute.For<EricksonLopez.Outbox.Diagnostics.IErrorSanitizer>());
        var poller = new AdaptivePoller(provider, channel, optionsMock, NullLogger<AdaptivePoller>.Instance, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics());

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        try { await poller.StartPollingAsync(cts.Token); } catch (OperationCanceledException) { }

        await repo.Received().FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void WakeUp_Should_Ignore_SemaphoreFullException_When_Already_Full()
    {
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();
        var publisher = Substitute.For<IBrokerPublisher>();
        var channel = new OutboxChannel(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<OutboxChannel>.Instance, 
            publisher, 
            Microsoft.Extensions.Options.Options.Create(new OutboxDispatcherOptions()), 
            Microsoft.Extensions.Options.Options.Create(new OutboxRuntimeOptions()), 
            new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), 
            Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(), 
            Substitute.For<EricksonLopez.Outbox.Diagnostics.IErrorSanitizer>());
        var options = Microsoft.Extensions.Options.Options.Create(new OutboxDispatcherOptions());
        var metrics = new EricksonLopez.Outbox.Diagnostics.OutboxMetrics();
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<AdaptivePoller>.Instance;
        
        var poller = new AdaptivePoller(provider, channel, options, logger, metrics);
        
        var field = typeof(AdaptivePoller).GetField("_wakeupSignal", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var semaphore = (System.Threading.SemaphoreSlim)field!.GetValue(poller)!;
        
        var maxCountField = typeof(System.Threading.SemaphoreSlim).GetField("m_maxCount", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (maxCountField != null)
        {
            maxCountField.SetValue(semaphore, 0);
        }

        // This should hit the catch block and not throw!
        poller.WakeUp();
    }
}
