// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Inbox.Configuration;
using EricksonLopez.Inbox.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace EricksonLopez.Inbox.Tests;

public sealed class InboxCleanupBackgroundServiceTests
{
    [Fact]
    public void Constructor_NullGuards_ThrowArgumentNullException()
    {
        var store = Substitute.For<IInboxStore>();
        var options = Options.Create(new InboxOptions());

        Action act1 = () => _ = new InboxCleanupBackgroundService(null!, options);
        act1.Should().Throw<ArgumentNullException>().WithParameterName("inboxStore");

        Action act2 = () => _ = new InboxCleanupBackgroundService(store, null!);
        act2.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void Constructor_WithOptionalParameters_InitializesCorrectly()
    {
        var store = Substitute.For<IInboxStore>();
        var options = Options.Create(new InboxOptions());

        var service = new InboxCleanupBackgroundService(store, options);
        service.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_WhenCleanupDisabled_LogsAndReturnsWithoutSweeping()
    {
        var store = Substitute.For<IInboxStore>();
        var options = Options.Create(new InboxOptions
        {
            EnableAutomaticCleanup = false
        });

        var logger = Substitute.For<ILogger<InboxCleanupBackgroundService>>();
        var service = new InboxCleanupBackgroundService(store, options, logger);
        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);
        if (service.ExecuteTask != null)
        {
            await service.ExecuteTask;
        }
        await service.StopAsync(CancellationToken.None);

        _ = store.DidNotReceiveWithAnyArgs().PurgeExpiredEntriesAsync(default, default);

        logger.Received(1).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Automatic inbox cleanup is disabled")),
            null,
            Arg.Any<Func<object, Exception?, string>>());

        logger.DidNotReceive().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Inbox cleanup background worker started")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenCancellationAlreadyRequested_ExitsLoopImmediately()
    {
        var store = Substitute.For<IInboxStore>();
        var fakeTime = new FakeTimeProvider();
        var options = Options.Create(new InboxOptions
        {
            EnableAutomaticCleanup = true,
            CleanupInterval = TimeSpan.FromMinutes(10),
            RetentionPeriod = TimeSpan.FromDays(3)
        });

        var service = new InboxCleanupBackgroundService(store, options, timeProvider: fakeTime);
        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);
        cts.Cancel();
        if (service.ExecuteTask != null)
        {
            try
            {
                await service.ExecuteTask;
            }
            catch (OperationCanceledException)
            {
                // Expected on cancellation
            }
        }
        await service.StopAsync(CancellationToken.None);

        _ = store.DidNotReceiveWithAnyArgs().PurgeExpiredEntriesAsync(default, default);
    }

    [Fact]
    public async Task ExecuteAsync_WhenEnabled_PurgesEntriesPeriodicallyBasedOnRetention_AndLogsLifecycle()
    {
        var store = Substitute.For<IInboxStore>();
        var fakeTime = new FakeTimeProvider();
        var startTime = new DateTimeOffset(2026, 8, 23, 12, 0, 0, TimeSpan.Zero);
        fakeTime.SetUtcNow(startTime);

        var options = Options.Create(new InboxOptions
        {
            EnableAutomaticCleanup = true,
            CleanupInterval = TimeSpan.FromMinutes(10),
            RetentionPeriod = TimeSpan.FromDays(3)
        });

        var logger = Substitute.For<ILogger<InboxCleanupBackgroundService>>();
        var service = new InboxCleanupBackgroundService(store, options, logger, fakeTime);
        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);
        await Task.Delay(50);

        logger.Received(1).Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Inbox cleanup background worker started") &&
                                o.ToString()!.Contains("00:10:00") &&
                                o.ToString()!.Contains("3.00:00:00")),
            null,
            Arg.Any<Func<object, Exception?, string>>());

        // Before interval expires (5 min < 10 min interval), store should NOT be called
        fakeTime.Advance(TimeSpan.FromMinutes(5));
        await Task.Delay(50);
        _ = store.DidNotReceiveWithAnyArgs().PurgeExpiredEntriesAsync(default, default);

        // Advance past first interval (total 10 min)
        fakeTime.Advance(TimeSpan.FromMinutes(5));
        await Task.Delay(50);

        var firstExpectedThreshold = startTime.AddMinutes(10).Subtract(TimeSpan.FromDays(3));
        _ = store.Received(1).PurgeExpiredEntriesAsync(
            Arg.Is<DateTimeOffset>(d => d == firstExpectedThreshold),
            Arg.Any<CancellationToken>());

        logger.Received(1).Log(
            LogLevel.Debug,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Purging inbox entries older than")),
            null,
            Arg.Any<Func<object, Exception?, string>>());

        logger.Received(1).Log(
            LogLevel.Debug,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Inbox sweep completed successfully")),
            null,
            Arg.Any<Func<object, Exception?, string>>());

        // Advance past second interval (another 10 min)
        fakeTime.Advance(TimeSpan.FromMinutes(10));
        await Task.Delay(50);

        var secondExpectedThreshold = startTime.AddMinutes(20).Subtract(TimeSpan.FromDays(3));
        _ = store.Received(1).PurgeExpiredEntriesAsync(
            Arg.Is<DateTimeOffset>(d => d == secondExpectedThreshold),
            Arg.Any<CancellationToken>());

        cts.Cancel();
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_WhenPurgeThrows_LogsErrorAndContinuesLoop()
    {
        var dbEx = new InvalidOperationException("DB connection error");
        var store = Substitute.For<IInboxStore>();
        store.PurgeExpiredEntriesAsync(default, default)
            .ThrowsForAnyArgs(dbEx);

        var fakeTime = new FakeTimeProvider();
        var options = Options.Create(new InboxOptions
        {
            EnableAutomaticCleanup = true,
            CleanupInterval = TimeSpan.FromMinutes(5),
            RetentionPeriod = TimeSpan.FromDays(1)
        });

        var logger = Substitute.For<ILogger<InboxCleanupBackgroundService>>();
        var service = new InboxCleanupBackgroundService(store, options, logger, fakeTime);

        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);

        await Task.Delay(50);
        fakeTime.Advance(TimeSpan.FromMinutes(6));
        await Task.Delay(50);

        _ = store.Received(1).PurgeExpiredEntriesAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());

        logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("An error occurred during inbox cleanup execution")),
            dbEx,
            Arg.Any<Func<object, Exception?, string>>());

        cts.Cancel();
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_WhenPurgeThrowsUncanceledOperationCanceledException_LogsErrorAndContinues()
    {
        var opEx = new OperationCanceledException("Timeout internal");
        var store = Substitute.For<IInboxStore>();
        store.PurgeExpiredEntriesAsync(default, default)
            .ThrowsForAnyArgs(opEx);

        var fakeTime = new FakeTimeProvider();
        var options = Options.Create(new InboxOptions
        {
            EnableAutomaticCleanup = true,
            CleanupInterval = TimeSpan.FromMinutes(5),
            RetentionPeriod = TimeSpan.FromDays(1)
        });

        var logger = Substitute.For<ILogger<InboxCleanupBackgroundService>>();
        var service = new InboxCleanupBackgroundService(store, options, logger, fakeTime);

        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);

        await Task.Delay(50);
        fakeTime.Advance(TimeSpan.FromMinutes(6));
        await Task.Delay(50);

        _ = store.Received(1).PurgeExpiredEntriesAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());

        logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("An error occurred during inbox cleanup execution")),
            opEx,
            Arg.Any<Func<object, Exception?, string>>());

        cts.Cancel();
        await service.StopAsync(CancellationToken.None);
    }
}
