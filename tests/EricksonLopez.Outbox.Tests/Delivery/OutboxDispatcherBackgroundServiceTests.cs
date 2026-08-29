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
using EricksonLopez.Outbox.Pipeline;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

#pragma warning disable CA2012
namespace EricksonLopez.Outbox.Tests.Delivery;

public class OutboxDispatcherBackgroundServiceTests
{
    private static (OutboxDispatcherBackgroundService service, IOutboxRepository repo, ILogger<OutboxDispatcherBackgroundService> logger) CreateService(OutboxDispatcherOptions? options = null)
    {
        var services = new ServiceCollection();
        var repo = Substitute.For<IOutboxRepository>();
        repo.FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IReadOnlyList<OutboxMessage>>(Array.Empty<OutboxMessage>()));
        repo.GetPendingCountAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(0L));
        repo.ReclaimStaleMessagesAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(0));
        services.AddScoped(_ => repo);
        var provider = services.BuildServiceProvider();

        options ??= new OutboxDispatcherOptions
        {
            MaxDegreeOfParallelism = 2,
            BatchSize = 10,
            UseAdaptivePolling = false,
            PollingInterval = TimeSpan.FromMilliseconds(50)
        };

        var publisher = Substitute.For<IBrokerPublisher>();
        var optionsMock = Options.Create(options);
        var channel = new OutboxChannel(
            NullLogger<OutboxChannel>.Instance,
            publisher,
            optionsMock,
            Options.Create(new OutboxRuntimeOptions()),
            new OutboxMetrics(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<IErrorSanitizer>(), TimeProvider.System);

        var poller = new AdaptivePoller(
            provider,
            channel,
            optionsMock,
            NullLogger<AdaptivePoller>.Instance,
            new OutboxMetrics(), TimeProvider.System);

        var logger = NullLogger<OutboxDispatcherBackgroundService>.Instance;
        var service = new OutboxDispatcherBackgroundService(
            logger,
            poller,
            channel,
            optionsMock, TimeProvider.System);

        return (service, repo, logger);
    }

    [Fact]
    public void IsRunning_Initial_Should_Be_False()
    {
        var (service, _, _) = CreateService();
        service.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_Should_Set_IsRunning_True_While_Running_And_False_After_Stop()
    {
        var (service, repo, _) = CreateService();

        using var cts = new CancellationTokenSource();
        var startTask = service.StartAsync(cts.Token);
        
        await service.WaitForRunningAsync(cts.Token);
        service.IsRunning.Should().BeTrue();

        await service.StopAsync(CancellationToken.None);
        service.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WhenParallelismIsZero_Should_DefaultToOneConsumer()
    {
        var options = new OutboxDispatcherOptions
        {
            MaxDegreeOfParallelism = 0,
            BatchSize = 10,
            UseAdaptivePolling = false,
            PollingInterval = TimeSpan.FromMilliseconds(50)
        };

        var (service, repo, _) = CreateService(options);

        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await service.WaitForRunningAsync(cts.Token);

        service.IsRunning.Should().BeTrue();

        await service.StopAsync(CancellationToken.None);
        service.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WhenParallelismIsNegative_Should_DefaultToOneConsumer()
    {
        var options = new OutboxDispatcherOptions
        {
            MaxDegreeOfParallelism = -5,
            BatchSize = 10,
            UseAdaptivePolling = false,
            PollingInterval = TimeSpan.FromMilliseconds(50)
        };

        var (service, repo, _) = CreateService(options);

        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await service.WaitForRunningAsync(cts.Token);

        service.IsRunning.Should().BeTrue();

        await service.StopAsync(CancellationToken.None);
        service.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WhenCancelledImmediately_Should_StopGracefully()
    {
        var (service, _, _) = CreateService();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await service.StartAsync(cts.Token);
        await service.StopAsync(CancellationToken.None);

        service.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WhenMultipleConsumers_ProcessesInParallel()
    {
        var services = new ServiceCollection();
        var repo = Substitute.For<IOutboxRepository>();
        repo.FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IReadOnlyList<OutboxMessage>>(Array.Empty<OutboxMessage>()));
        repo.GetPendingCountAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(0L));
        repo.ReclaimStaleMessagesAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(0));
        services.AddScoped(_ => repo);
        var provider = services.BuildServiceProvider();

        var options = new OutboxDispatcherOptions
        {
            MaxDegreeOfParallelism = 3,
            BatchSize = 10,
            UseAdaptivePolling = false,
            PollingInterval = TimeSpan.FromMilliseconds(50)
        };
        var optionsMock = Options.Create(options);

        var publisher = Substitute.For<IBrokerPublisher>();
        int processedCount = 0;
        var processedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns(_ => {
                if (Interlocked.Increment(ref processedCount) >= 3) processedTcs.TrySetResult();
                return ValueTask.FromResult(DispatchResult.Ok());
            });

        var channel = new OutboxChannel(
            NullLogger<OutboxChannel>.Instance,
            publisher,
            optionsMock,
            Options.Create(new OutboxRuntimeOptions()),
            new OutboxMetrics(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<IErrorSanitizer>(), TimeProvider.System);

        var poller = new AdaptivePoller(provider, channel, optionsMock, NullLogger<AdaptivePoller>.Instance, new OutboxMetrics(), TimeProvider.System);

        var service = new OutboxDispatcherBackgroundService(
            NullLogger<OutboxDispatcherBackgroundService>.Instance,
            poller,
            channel,
            optionsMock, TimeProvider.System);

        for (int i = 0; i < 3; i++)
        {
            var msg = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
            await channel.WriteAsync(msg, CancellationToken.None);
        }

        await service.StartAsync(CancellationToken.None);

        await processedTcs.Task.WaitAsync(TimeSpan.FromSeconds(15));

        Volatile.Read(ref processedCount).Should().Be(3);
        service.IsRunning.Should().BeTrue();

        await service.StopAsync(CancellationToken.None);
        service.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WhenConsumerThrows_CatchesAndRetries()
    {
        var services = new ServiceCollection();
        var repo = Substitute.For<IOutboxRepository>();
        repo.FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IReadOnlyList<OutboxMessage>>(Array.Empty<OutboxMessage>()));
        services.AddScoped(_ => repo);
        var provider = services.BuildServiceProvider();

        var options = new OutboxDispatcherOptions
        {
            MaxDegreeOfParallelism = 1,
            BatchSize = 10,
            UseAdaptivePolling = false,
            PollingInterval = TimeSpan.FromMilliseconds(50)
        };
        var optionsMock = Options.Create(options);

        var publisher = Substitute.For<IBrokerPublisher>();
        int pubCount = 0;
        var pubCountTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        publisher.PublishRawAsync(Arg.Any<OutboxMessage>(), Arg.Any<OutboxMessageMetadata>(), Arg.Any<DispatchContext>())
            .Returns<ValueTask<DispatchResult>>(_ => {
                pubCount++;
                pubCountTcs.TrySetResult();
                throw new InvalidOperationException("Simulated publisher crash");
            });

        var channel = new OutboxChannel(
            NullLogger<OutboxChannel>.Instance,
            publisher,
            optionsMock,
            Options.Create(new OutboxRuntimeOptions()),
            new OutboxMetrics(),
            provider.GetRequiredService<IServiceScopeFactory>(),
            Substitute.For<IErrorSanitizer>(), TimeProvider.System);

        var poller = new AdaptivePoller(provider, channel, optionsMock, NullLogger<AdaptivePoller>.Instance, new OutboxMetrics(), TimeProvider.System);

        var service = new OutboxDispatcherBackgroundService(
            NullLogger<OutboxDispatcherBackgroundService>.Instance,
            poller,
            channel,
            optionsMock, TimeProvider.System);

        var msg = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
        await channel.WriteAsync(msg, CancellationToken.None);

        using var cts = new CancellationTokenSource();
        var task = service.StartAsync(cts.Token);
        await pubCountTcs.Task.WaitAsync(TimeSpan.FromSeconds(15));

        service.IsRunning.Should().BeTrue();

        cts.Cancel();
        await service.StopAsync(CancellationToken.None);
    }

    private sealed class TrackingDispatcherLogger : ILogger<OutboxDispatcherBackgroundService>
    {
        public int ConsumerStartedCount;
        public int ConsumerStoppedCount;
        public int ConsumerCrashedCount;
        public readonly TaskCompletionSource ConsumerStartedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource ConsumerStoppedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource ConsumerCrashedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int RequiredStartedCount = 2;
        public int RequiredStoppedCount = 2;
        public int RequiredCrashedCount = 1;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (eventId.Id == 10106)
            {
                if (Interlocked.Increment(ref ConsumerStartedCount) >= RequiredStartedCount)
                    ConsumerStartedTcs.TrySetResult();
            }
            if (eventId.Id == 10107)
            {
                if (Interlocked.Increment(ref ConsumerStoppedCount) >= RequiredStoppedCount)
                    ConsumerStoppedTcs.TrySetResult();
            }
            if (eventId.Id == 10105)
            {
                if (Interlocked.Increment(ref ConsumerCrashedCount) >= RequiredCrashedCount)
                    ConsumerCrashedTcs.TrySetResult();
            }
        }
    }

    [Fact]
    public async Task ExecuteAsync_ExactConsumerCountStarted_And_Stopped()
    {
        var services = new ServiceCollection();
        var repo = Substitute.For<IOutboxRepository>();
        repo.FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult<IReadOnlyList<OutboxMessage>>(Array.Empty<OutboxMessage>()));
        repo.GetPendingCountAsync(Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(0L));
        repo.ReclaimStaleMessagesAsync(Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(0));
        services.AddScoped(_ => repo);
        var provider = services.BuildServiceProvider();

        var options = new OutboxDispatcherOptions
        {
            MaxDegreeOfParallelism = 2,
            BatchSize = 10,
            UseAdaptivePolling = false,
            PollingInterval = TimeSpan.FromMilliseconds(50)
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
            Substitute.For<IErrorSanitizer>(), TimeProvider.System);

        var poller = new AdaptivePoller(provider, channel, optionsMock, NullLogger<AdaptivePoller>.Instance, new OutboxMetrics(), TimeProvider.System);

        var trackingLogger = new TrackingDispatcherLogger { RequiredStartedCount = 2, RequiredStoppedCount = 2 };
        var service = new OutboxDispatcherBackgroundService(
            trackingLogger,
            poller,
            channel,
            optionsMock, TimeProvider.System);

        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);

        await trackingLogger.ConsumerStartedTcs.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Volatile.Read(ref trackingLogger.ConsumerStartedCount).Should().Be(2);

        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        await trackingLogger.ConsumerStoppedTcs.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Volatile.Read(ref trackingLogger.ConsumerStoppedCount).Should().Be(2);
    }

    [Fact]
    public async Task ExecuteAsync_WhenConsumerThrows_LogsCrashedAndRecovers()
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        int scopeCount = 0;
        scopeFactory.CreateScope().Returns(_ => {
            scopeCount++;
            if (scopeCount == 1) throw new InvalidOperationException("Fatal scope crash");
            var scope = Substitute.For<IServiceScope>();
            var sp = Substitute.For<IServiceProvider>();
            var repo = Substitute.For<IOutboxRepository>();
            repo.FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(ValueTask.FromResult<IReadOnlyList<OutboxMessage>>(Array.Empty<OutboxMessage>()));
            sp.GetService(typeof(IOutboxRepository)).Returns(repo);
            scope.ServiceProvider.Returns(sp);
            return scope;
        });

        var options = new OutboxDispatcherOptions
        {
            MaxDegreeOfParallelism = 1,
            BatchSize = 10,
            UseAdaptivePolling = false,
            PollingInterval = TimeSpan.FromMilliseconds(50)
        };
        var optionsMock = Options.Create(options);

        var publisher = Substitute.For<IBrokerPublisher>();
        var channel = new OutboxChannel(
            NullLogger<OutboxChannel>.Instance,
            publisher,
            optionsMock,
            Options.Create(new OutboxRuntimeOptions()),
            new OutboxMetrics(),
            scopeFactory,
            Substitute.For<IErrorSanitizer>(), TimeProvider.System);

        var poller = new AdaptivePoller(Substitute.For<IServiceProvider>(), channel, optionsMock, NullLogger<AdaptivePoller>.Instance, new OutboxMetrics(), TimeProvider.System);

        var trackingLogger = new TrackingDispatcherLogger { RequiredCrashedCount = 1 };
        var service = new OutboxDispatcherBackgroundService(
            trackingLogger,
            poller,
            channel,
            optionsMock, TimeProvider.System);

        var msg = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
        await channel.WriteAsync(msg, CancellationToken.None);

        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);

        await trackingLogger.ConsumerCrashedTcs.Task.WaitAsync(TimeSpan.FromSeconds(15));
        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        Volatile.Read(ref trackingLogger.ConsumerCrashedCount).Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task ExecuteAsync_WhenStoppingTokenCancelledDuringCrashDelay_BreaksGracefully()
    {
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(_ => throw new InvalidOperationException("Fatal crash"));

        var options = new OutboxDispatcherOptions
        {
            MaxDegreeOfParallelism = 1,
            BatchSize = 10,
            UseAdaptivePolling = false,
            PollingInterval = TimeSpan.FromMilliseconds(50)
        };
        var optionsMock = Options.Create(options);

        var publisher = Substitute.For<IBrokerPublisher>();
        var channel = new OutboxChannel(
            NullLogger<OutboxChannel>.Instance,
            publisher,
            optionsMock,
            Options.Create(new OutboxRuntimeOptions()),
            new OutboxMetrics(),
            scopeFactory,
            Substitute.For<IErrorSanitizer>(), TimeProvider.System);

        var poller = new AdaptivePoller(Substitute.For<IServiceProvider>(), channel, optionsMock, NullLogger<AdaptivePoller>.Instance, new OutboxMetrics(), TimeProvider.System);

        var trackingLogger = new TrackingDispatcherLogger { RequiredCrashedCount = 1 };
        var service = new OutboxDispatcherBackgroundService(
            trackingLogger,
            poller,
            channel,
            optionsMock, TimeProvider.System);

        var msg = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
        await channel.WriteAsync(msg, CancellationToken.None);

        using var cts = new CancellationTokenSource();
        var startTask = service.StartAsync(cts.Token);

        await trackingLogger.ConsumerCrashedTcs.Task.WaitAsync(TimeSpan.FromSeconds(15));
        cts.Cancel();

        await service.StopAsync(CancellationToken.None);
        Volatile.Read(ref trackingLogger.ConsumerCrashedCount).Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task WaitForRunningAsync_WhenAlreadyRunning_ReturnsImmediately()
    {
        var (service, _, _) = CreateService();
        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        var started = await service.WaitForRunningAsync(cts.Token);
        started.Should().BeTrue();
        service.IsRunning.Should().BeTrue();

        var fastTask = service.WaitForRunningAsync(cts.Token);
        fastTask.IsCompletedSuccessfully.Should().BeTrue();
        var result = await fastTask;
        result.Should().BeTrue();

        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_WhenConsumerDelayCompletes_RecoversAndContinuesLoop()
    {
        var timeProvider = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        int scopeCount = 0;
        var secondScopeCalledTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        scopeFactory.CreateScope().Returns(_ =>
        {
            var count = Interlocked.Increment(ref scopeCount);
            if (count == 1) throw new InvalidOperationException("First scope crash");

            secondScopeCalledTcs.TrySetResult(true);
            var scope = Substitute.For<IServiceScope>();
            var sp = Substitute.For<IServiceProvider>();
            var repo = Substitute.For<IOutboxRepository>();
            repo.FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(ValueTask.FromResult<IReadOnlyList<OutboxMessage>>(Array.Empty<OutboxMessage>()));
            sp.GetService(typeof(IOutboxRepository)).Returns(repo);
            scope.ServiceProvider.Returns(sp);
            return scope;
        });

        var options = new OutboxDispatcherOptions
        {
            MaxDegreeOfParallelism = 1,
            BatchSize = 10,
            UseAdaptivePolling = false,
            PollingInterval = TimeSpan.FromMilliseconds(50)
        };
        var optionsMock = Options.Create(options);

        var publisher = Substitute.For<IBrokerPublisher>();
        var channel = new OutboxChannel(
            NullLogger<OutboxChannel>.Instance,
            publisher,
            optionsMock,
            Options.Create(new OutboxRuntimeOptions()),
            new OutboxMetrics(),
            scopeFactory,
            Substitute.For<IErrorSanitizer>(),
            timeProvider);

        var poller = new AdaptivePoller(Substitute.For<IServiceProvider>(), channel, optionsMock, NullLogger<AdaptivePoller>.Instance, new OutboxMetrics(), timeProvider);

        var trackingLogger = new TrackingDispatcherLogger { RequiredCrashedCount = 1 };
        var service = new OutboxDispatcherBackgroundService(
            trackingLogger,
            poller,
            channel,
            optionsMock,
            timeProvider);

        var msg1 = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
        await channel.WriteAsync(msg1, CancellationToken.None);

        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);

        await trackingLogger.ConsumerCrashedTcs.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Volatile.Read(ref trackingLogger.ConsumerCrashedCount).Should().Be(1);

        // Write msg2 now while consumer is in delay so it is ready for the second iteration
        var msg2 = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
        await channel.WriteAsync(msg2, CancellationToken.None);

        // Advance FakeTimeProvider by 5 seconds to complete the crash delay and trigger recovery loop
        timeProvider.Advance(TimeSpan.FromSeconds(5));

        await secondScopeCalledTcs.Task.WaitAsync(TimeSpan.FromSeconds(15));
        Volatile.Read(ref scopeCount).Should().BeGreaterThanOrEqualTo(2);

        cts.Cancel();
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCancellationRequested_StopsAllConsumersAndLogsStopped()
    {
        var (service, _, _) = CreateService(new OutboxDispatcherOptions
        {
            MaxDegreeOfParallelism = 2,
            BatchSize = 10,
            UseAdaptivePolling = false,
            PollingInterval = TimeSpan.FromMilliseconds(50)
        });

        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await service.WaitForRunningAsync(cts.Token);

        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        service.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WhenConsumerCrashesAndCancelledDuringDelay_ExitsGracefully()
    {
        var timeProvider = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(_ => throw new InvalidOperationException("Scope crash"));

        var options = new OutboxDispatcherOptions { MaxDegreeOfParallelism = 1, ChannelCapacity = 10 };
        var optionsMock = Options.Create(options);

        var publisher = Substitute.For<IBrokerPublisher>();
        var channel = new OutboxChannel(
            NullLogger<OutboxChannel>.Instance,
            publisher,
            optionsMock,
            Options.Create(new OutboxRuntimeOptions()),
            new OutboxMetrics(),
            scopeFactory,
            Substitute.For<IErrorSanitizer>(),
            timeProvider);

        var poller = new AdaptivePoller(Substitute.For<IServiceProvider>(), channel, optionsMock, NullLogger<AdaptivePoller>.Instance, new OutboxMetrics(), timeProvider);

        var trackingLogger = new TrackingDispatcherLogger { RequiredCrashedCount = 1 };
        var service = new OutboxDispatcherBackgroundService(
            trackingLogger,
            poller,
            channel,
            optionsMock,
            timeProvider);

        var msg = new OutboxMessage(Guid.NewGuid(), "Type", ReadOnlyMemory<byte>.Empty, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, OutboxMessageStatus.Pending, 0, null);
        await channel.WriteAsync(msg, CancellationToken.None);

        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);

        await trackingLogger.ConsumerCrashedTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Cancel stopping token while in Task.Delay(5000)
        cts.Cancel();

        await service.StopAsync(CancellationToken.None);
        service.IsRunning.Should().BeFalse();
    }
}





