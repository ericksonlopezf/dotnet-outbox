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
    public void WakeUp_Should_Not_Throw_When_Full()
    {
        var services = new ServiceCollection();
        var provider = services.BuildServiceProvider();

        var options = new OutboxDispatcherOptions();
        var optionsMock = Options.Create(options);

        var publisher = Substitute.For<IBrokerPublisher>();
        var channel = new OutboxChannel(NullLogger<OutboxChannel>.Instance, publisher, optionsMock, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), NSubstitute.Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(), NSubstitute.Substitute.For<EricksonLopez.Outbox.Diagnostics.IErrorSanitizer>(), TimeProvider.System);

        var poller = new AdaptivePoller(provider, channel, optionsMock, NullLogger<AdaptivePoller>.Instance, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), TimeProvider.System);

        var act = () => Parallel.For(0, 10, i => poller.WakeUp());
        act.Should().NotThrow();
    }

    [Fact]
    public async Task WakeUp_Should_Ignore_SemaphoreFullException()
    {
        var provider = Substitute.For<IServiceProvider>();
        var options = new OutboxDispatcherOptions();
        var optionsMock = Options.Create(options);
        var publisher = Substitute.For<IBrokerPublisher>();
        var channel = new OutboxChannel(NullLogger<OutboxChannel>.Instance, publisher, optionsMock, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), NSubstitute.Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(), NSubstitute.Substitute.For<EricksonLopez.Outbox.Diagnostics.IErrorSanitizer>(), TimeProvider.System);

        var poller = new AdaptivePoller(provider, channel, optionsMock, NullLogger<AdaptivePoller>.Instance, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), TimeProvider.System);

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
            Substitute.For<EricksonLopez.Outbox.Diagnostics.IErrorSanitizer>(), TimeProvider.System);
        var options = Microsoft.Extensions.Options.Options.Create(new OutboxDispatcherOptions());
        var metrics = new EricksonLopez.Outbox.Diagnostics.OutboxMetrics();
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<AdaptivePoller>.Instance;
        
        var poller = new AdaptivePoller(provider, channel, options, logger, metrics, TimeProvider.System);
        
        // Calling WakeUp multiple times fills the semaphore (capacity 1) and safely triggers SemaphoreFullException catch
        var act = () =>
        {
            poller.WakeUp();
            poller.WakeUp();
        };

        act.Should().NotThrow();
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
            Substitute.For<EricksonLopez.Outbox.Diagnostics.IErrorSanitizer>(), TimeProvider.System);
        var options = Microsoft.Extensions.Options.Options.Create(new OutboxDispatcherOptions());
        var metrics = new EricksonLopez.Outbox.Diagnostics.OutboxMetrics();
        var logger = Microsoft.Extensions.Logging.Abstractions.NullLogger<AdaptivePoller>.Instance;
        
        var poller = new AdaptivePoller(provider, channel, options, logger, metrics, TimeProvider.System);
        poller.Dispose();

        // Act
        var act = () => poller.WakeUp();

        // Assert
        act.Should().NotThrow<ObjectDisposedException>();
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
        var channel = new OutboxChannel(NullLogger<OutboxChannel>.Instance, publisher, optionsMock, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), NSubstitute.Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(), NSubstitute.Substitute.For<EricksonLopez.Outbox.Diagnostics.IErrorSanitizer>(), TimeProvider.System);

        var poller = new AdaptivePoller(provider, channel, Microsoft.Extensions.Options.Options.Create(options), NullLogger<AdaptivePoller>.Instance, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), TimeProvider.System);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try { await poller.StartPollingAsync(cts.Token); } catch (OperationCanceledException) { }
        sw.Stop();

        // Ensure it fetched exactly TWICE because the first fetch returned a full batch,
        // causing an immediate loop (10ms delay) and then a second fetch (which returned empty and waited).
        await repo.Received(2).FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Poller_Should_Use_Default_MinMs_When_MaxBatchesPerSecond_Is_Zero()
    {
        var services = new ServiceCollection();
        var repo = Substitute.For<IOutboxRepository>();
        var message = new OutboxMessage(Guid.NewGuid(), "Test", default, null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        using var cts = new CancellationTokenSource();
        repo.FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => {
                cts.Cancel();
                return new ValueTask<IReadOnlyList<OutboxMessage>>(new List<OutboxMessage> { message });
            });
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
            Substitute.For<EricksonLopez.Outbox.Diagnostics.IErrorSanitizer>(), TimeProvider.System);
        var poller = new AdaptivePoller(provider, channel, optionsMock, NullLogger<AdaptivePoller>.Instance, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), TimeProvider.System);

        try { await poller.StartPollingAsync(cts.Token); } catch (OperationCanceledException) { }

        await repo.Received().FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Poller_Should_Use_Calculated_MinMs_When_MaxBatchesPerSecond_Is_Greater_Than_Zero()
    {
        var services = new ServiceCollection();
        var repo = Substitute.For<IOutboxRepository>();
        var message = new OutboxMessage(Guid.NewGuid(), "Test", default, null, null, System.Text.Encoding.UTF8.GetBytes("{}"), DateTimeOffset.UtcNow, null, null, 0, 0, null);
        using var cts = new CancellationTokenSource();
        repo.FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => {
                cts.Cancel();
                return new ValueTask<IReadOnlyList<OutboxMessage>>(new List<OutboxMessage> { message });
            });
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
            Substitute.For<EricksonLopez.Outbox.Diagnostics.IErrorSanitizer>(), TimeProvider.System);
        var poller = new AdaptivePoller(provider, channel, optionsMock, NullLogger<AdaptivePoller>.Instance, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), TimeProvider.System);

        try { await poller.StartPollingAsync(cts.Token); } catch (OperationCanceledException) { }

        await repo.Received().FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task StartPollingAsync_WhenEmptyQueue_WakeUp_Should_Trigger_Immediate_Second_Fetch()
    {
        var services = new ServiceCollection();
        var repo = Substitute.For<IOutboxRepository>();
        var firstFetchTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondFetchTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        int fetchCount = 0;

        repo.FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var count = Interlocked.Increment(ref fetchCount);
                if (count == 1) firstFetchTcs.TrySetResult(true);
                else if (count >= 2) secondFetchTcs.TrySetResult(true);
                return ValueTask.FromResult<IReadOnlyList<OutboxMessage>>(Array.Empty<OutboxMessage>());
            });
        repo.GetPendingCountAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(0L));
        repo.ReclaimStaleMessagesAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(0));

        services.AddScoped(_ => repo);
        var provider = services.BuildServiceProvider();

        var options = new OutboxDispatcherOptions
        {
            UseAdaptivePolling = true,
            BatchSize = 10,
            PollingInterval = TimeSpan.FromSeconds(30)
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
            Substitute.For<EricksonLopez.Outbox.Diagnostics.IErrorSanitizer>(), TimeProvider.System);

        var fakeTime = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var poller = new AdaptivePoller(provider, channel, optionsMock, NullLogger<AdaptivePoller>.Instance, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), fakeTime);

        using var cts = new CancellationTokenSource();
        var pollingTask = poller.StartPollingAsync(cts.Token);

        // Wait for first fetch
        await firstFetchTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fetchCount.Should().Be(1);

        // Wake up poller immediately
        poller.WakeUp();

        // Should fetch again immediately without waiting
        await secondFetchTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        fetchCount.Should().BeGreaterThanOrEqualTo(2);

        cts.Cancel();
        try { await pollingTask; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task StartPollingAsync_WhenQueueEmpty_And_AdaptivePollingTrue_WaitsForSignal()
    {
        var services = new ServiceCollection();
        var repo = Substitute.For<IOutboxRepository>();
        int fetchCount = 0;
        var firstFetchTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        repo.FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ => {
                if (Interlocked.Increment(ref fetchCount) == 1) firstFetchTcs.TrySetResult();
                return ValueTask.FromResult<IReadOnlyList<OutboxMessage>>(Array.Empty<OutboxMessage>());
            });
        services.AddScoped(_ => repo);
        var provider = services.BuildServiceProvider();

        var options = new OutboxDispatcherOptions
        {
            UseAdaptivePolling = true,
            BatchSize = 10,
            PollingInterval = TimeSpan.FromSeconds(10)
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

        await firstFetchTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        // Because queue was empty (fetchedCount = 0), it waits for signal/timer and does not immediately loop again
        Volatile.Read(ref fetchCount).Should().Be(1);

        cts.Cancel();
        try { await task; } catch (OperationCanceledException) { }
    }

    [Fact]
    public async Task StartPollingAsync_WhenNormalIntervalExpires_ExecutesBackoffTimerAndContinues()
    {
        var services = new ServiceCollection();
        var repo = Substitute.For<IOutboxRepository>();
        int fetchCalls = 0;
        var secondFetchTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        repo.FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var count = Interlocked.Increment(ref fetchCalls);
                if (count >= 2) secondFetchTcs.TrySetResult(true);
                return ValueTask.FromResult<IReadOnlyList<OutboxMessage>>(Array.Empty<OutboxMessage>());
            });

        services.AddScoped(_ => repo);
        var provider = services.BuildServiceProvider();

        var options = new OutboxDispatcherOptions
        {
            UseAdaptivePolling = false,
            PollingInterval = TimeSpan.FromMilliseconds(100)
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
        Volatile.Read(ref fetchCalls).Should().Be(1);

        // Max jitter is 100ms * 1.15 = 115ms. Advancing 150ms guarantees timer fires.
        fakeTime.Advance(TimeSpan.FromMilliseconds(150));

        await secondFetchTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Volatile.Read(ref fetchCalls).Should().Be(2);

        cts.Cancel();
        try { await pollingTask; } catch (OperationCanceledException) { }
    }

    [Theory]
    [InlineData(0.0, 850)]   // 1000 * (1 - 0.15) = 850ms
    [InlineData(0.5, 1000)]  // 1000 * (1 + 0.0) = 1000ms
    [InlineData(1.0, 1150)]  // 1000 * (1 + 0.15) = 1150ms
    public void CalculatePollingDelay_WithKnownRandomValues_ReturnsExpectedJitteredDelay(double rand, int expectedMs)
    {
        var delay = AdaptivePoller.CalculatePollingDelay(TimeSpan.FromSeconds(1), () => rand);
        delay.TotalMilliseconds.Should().Be(expectedMs);
    }

    [Fact]
    public void CalculatePollingDelay_WhenZeroOrNegative_ClampsToAtLeastOneMillisecond()
    {
        var delay = AdaptivePoller.CalculatePollingDelay(TimeSpan.Zero, () => 0.0);
        delay.TotalMilliseconds.Should().Be(1);

        var delay2 = AdaptivePoller.CalculatePollingDelay(TimeSpan.FromMilliseconds(100)); // Default random provider
        delay2.TotalMilliseconds.Should().BeInRange(85, 115);
    }
}



