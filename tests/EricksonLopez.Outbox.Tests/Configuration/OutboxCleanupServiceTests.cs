// Copyright © Erickson Lopez. MIT License.
#pragma warning disable CA2012
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox.Hosting;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Testing;
using EricksonLopez.Result;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Configuration;

public class OutboxCleanupServiceTests
{
    [Fact]
    public void Constructor_NullServiceProvider_ThrowsArgumentNullException()
    {
        var options = Options.Create(new OutboxCleanupOptions());
        var logger = NullLogger<OutboxCleanupService>.Instance;

        var act = () => new OutboxCleanupService(null!, options, logger);
        act.Should().Throw<ArgumentNullException>().WithParameterName("serviceProvider");
    }

    [Fact]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        var services = Substitute.For<IServiceProvider>();
        var logger = NullLogger<OutboxCleanupService>.Instance;

        var act = () => new OutboxCleanupService(services, null!, logger);
        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var services = Substitute.For<IServiceProvider>();
        var options = Options.Create(new OutboxCleanupOptions());

        var act = () => new OutboxCleanupService(services, options, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task ExecuteAsync_WhenDisabled_LogsDisabledAndExitsImmediately()
    {
        var services = new ServiceCollection();
        var sp = services.BuildServiceProvider();
        var options = Options.Create(new OutboxCleanupOptions { Enabled = false });
        var logger = Substitute.For<ILogger<OutboxCleanupService>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var fakeTime = new FakeTimeProvider();

        var service = new OutboxCleanupService(sp, options, logger, fakeTime);
        using var cts = new CancellationTokenSource();

        await service.StartAsync(cts.Token);
        if (service.ExecuteTask != null)
        {
            await service.ExecuteTask;
        }
        await service.StopAsync(CancellationToken.None);

        var calls = logger.ReceivedCalls().ToList();
        calls.Should().NotBeEmpty();
        calls.Any(c => c.GetArguments().Any(a => a?.ToString()?.Contains("Outbox Cleanup Service is disabled") == true)).Should().BeTrue();
    }

    [Fact]
    public async Task PerformCleanupAsync_WhenRepositoryRegistered_PurgesDispatchedMessagesAndLogsInformation()
    {
        var repository = Substitute.For<IOutboxRepository>();
        repository.PurgeDispatchedMessagesAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<int>(5));

        var services = new ServiceCollection();
        services.AddScoped<IOutboxRepository>(_ => repository);
        var sp = services.BuildServiceProvider();

        var options = Options.Create(new OutboxCleanupOptions
        {
            Enabled = true,
            CleanupInterval = TimeSpan.FromMinutes(1),
            RetentionPeriod = TimeSpan.FromDays(3),
            BatchSize = 500
        });

        var logger = Substitute.For<ILogger<OutboxCleanupService>>();
        logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        var fixedTime = new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);
        var fakeTime = new FakeTimeProvider(fixedTime);
        var service = new OutboxCleanupService(sp, options, logger, fakeTime);

        var result = await service.PerformCleanupAsync(CancellationToken.None);

        result.Should().Be(5);
        var expectedCutoff = fixedTime - TimeSpan.FromDays(3);
        await repository.Received(1).PurgeDispatchedMessagesAsync(
            Arg.Is<DateTimeOffset>(d => d == expectedCutoff),
            Arg.Is(500),
            Arg.Any<CancellationToken>());

        logger.ReceivedCalls().Any(c => c.GetMethodInfo().Name == "Log" &&
            c.GetArguments()[0]?.Equals(LogLevel.Information) == true &&
            c.GetArguments()[2]?.ToString()!.Contains("Purged 5 dispatched outbox messages") == true).Should().BeTrue();
    }

    [Fact]
    public async Task PerformCleanupAsync_WhenPurgeReturnsZero_DoesNotLogPurgedAndReturnsZero()
    {
        var repository = Substitute.For<IOutboxRepository>();
        repository.PurgeDispatchedMessagesAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<int>(0));

        var services = new ServiceCollection();
        services.AddScoped<IOutboxRepository>(_ => repository);
        var sp = services.BuildServiceProvider();

        var options = Options.Create(new OutboxCleanupOptions
        {
            Enabled = true,
            CleanupInterval = TimeSpan.FromMinutes(1),
            RetentionPeriod = TimeSpan.FromDays(7),
            BatchSize = 100
        });

        var logger = Substitute.For<ILogger<OutboxCleanupService>>();
        var fakeTime = new FakeTimeProvider();
        var service = new OutboxCleanupService(sp, options, logger, fakeTime);

        var result = await service.PerformCleanupAsync(CancellationToken.None);

        result.Should().Be(0);
        await repository.Received(1).PurgeDispatchedMessagesAsync(
            Arg.Any<DateTimeOffset>(),
            Arg.Is(100),
            Arg.Any<CancellationToken>());

        logger.DidNotReceive().Log(
            LogLevel.Information,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Purged")),
            null,
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenEnabled_RunsPeriodicPurgeAndHandlesExceptions()
    {
        var repository = Substitute.For<IOutboxRepository>();
        var callCount = 0;
        var tcs1 = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcs2 = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcs3 = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        repository.PurgeDispatchedMessagesAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                callCount++;
                if (callCount == 1) { tcs1.TrySetResult(true); return new ValueTask<int>(10); }
                if (callCount == 2) { tcs2.TrySetResult(true); throw new InvalidOperationException("DB Timeout"); }
                if (callCount == 3) { tcs3.TrySetResult(true); return new ValueTask<int>(0); }
                return new ValueTask<int>(0);
            });

        var services = new ServiceCollection();
        services.AddScoped<IOutboxRepository>(_ => repository);
        var sp = services.BuildServiceProvider();

        var options = Options.Create(new OutboxCleanupOptions
        {
            Enabled = true,
            CleanupInterval = TimeSpan.FromMinutes(5),
            RetentionPeriod = TimeSpan.FromDays(1),
            BatchSize = 100
        });

        var logger = Substitute.For<ILogger<OutboxCleanupService>>();
        var fakeTime = new FakeTimeProvider();
        var service = new OutboxCleanupService(sp, options, logger, fakeTime);

        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await Task.Delay(50);

        // Before advancing time, Task.Delay must prevent premature execution
        callCount.Should().Be(0);

        // Advance 1st cycle -> Purges 10
        fakeTime.Advance(TimeSpan.FromMinutes(5));
        await tcs1.Task.WaitAsync(TimeSpan.FromSeconds(5));
        callCount.Should().Be(1);

        // Advance 2nd cycle -> Throws and logs error
        fakeTime.Advance(TimeSpan.FromMinutes(5));
        await tcs2.Task.WaitAsync(TimeSpan.FromSeconds(5));
        callCount.Should().Be(2);

        // Advance 3rd cycle -> Purges 0
        fakeTime.Advance(TimeSpan.FromMinutes(5));
        await tcs3.Task.WaitAsync(TimeSpan.FromSeconds(5));
        callCount.Should().Be(3);

        await service.StopAsync(CancellationToken.None);

        logger.Received(1).Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Error occurred while executing Outbox Cleanup pass")),
            Arg.Any<InvalidOperationException>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task PerformCleanupAsync_WhenNoRepositoryRegistered_ReturnsZero()
    {
        var services = new ServiceCollection();
        var sp = services.BuildServiceProvider();

        var options = Options.Create(new OutboxCleanupOptions
        {
            Enabled = true,
            CleanupInterval = TimeSpan.FromMinutes(1)
        });

        var logger = NullLogger<OutboxCleanupService>.Instance;
        var fakeTime = new FakeTimeProvider();
        var service = new OutboxCleanupService(sp, options, logger, fakeTime);

        var result = await service.PerformCleanupAsync(CancellationToken.None);

        result.Should().Be(0);
    }

    [Fact]
    public async Task PerformCleanupAsync_WhenRepositoryThrows_PropagatesException()
    {
        var repository = Substitute.For<IOutboxRepository>();
        repository.PurgeDispatchedMessagesAsync(Arg.Any<DateTimeOffset>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<int>>(_ => throw new InvalidOperationException("DB Timeout"));

        var services = new ServiceCollection();
        services.AddScoped<IOutboxRepository>(_ => repository);
        var sp = services.BuildServiceProvider();

        var options = Options.Create(new OutboxCleanupOptions
        {
            Enabled = true,
            CleanupInterval = TimeSpan.FromMinutes(1)
        });

        var logger = NullLogger<OutboxCleanupService>.Instance;
        var fakeTime = new FakeTimeProvider();
        var service = new OutboxCleanupService(sp, options, logger, fakeTime);

        var act = () => service.PerformCleanupAsync(CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("DB Timeout");
    }

    [Fact]
    public async Task ExecuteAsync_WhenEnabled_StartsAndStopsCleanly()
    {
        var services = new ServiceCollection();
        var sp = services.BuildServiceProvider();

        var options = Options.Create(new OutboxCleanupOptions
        {
            Enabled = true,
            CleanupInterval = TimeSpan.FromHours(1),
            RetentionPeriod = TimeSpan.FromDays(1)
        });

        var logger = NullLogger<OutboxCleanupService>.Instance;
        var service = new OutboxCleanupService(sp, options, logger);

        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_WhenCancelledImmediately_ExitsCleanly()
    {
        var services = new ServiceCollection();
        var sp = services.BuildServiceProvider();

        var options = Options.Create(new OutboxCleanupOptions
        {
            Enabled = true,
            CleanupInterval = TimeSpan.FromHours(1)
        });

        var logger = NullLogger<OutboxCleanupService>.Instance;
        var service = new OutboxCleanupService(sp, options, logger);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await service.StartAsync(cts.Token);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public void AddOutboxCleanupService_WithConfiguration_RegistersHostedServiceWithOptions()
    {
        var services = new ServiceCollection();
        services.AddOutboxCleanupService(opt =>
        {
            opt.Enabled = true;
            opt.RetentionPeriod = TimeSpan.FromDays(14);
        });

        var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<OutboxCleanupOptions>>().Value;

        options.Enabled.Should().BeTrue();
        options.RetentionPeriod.Should().Be(TimeSpan.FromDays(14));
    }
}




