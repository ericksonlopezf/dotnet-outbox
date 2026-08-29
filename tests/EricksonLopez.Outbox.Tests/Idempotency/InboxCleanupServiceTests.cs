// Copyright © Erickson Lopez. MIT License.
#pragma warning disable CA2012
#pragma warning disable CA1806
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Idempotency;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Result;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Idempotency;

public class InboxCleanupServiceTests
{
    [Fact]
    public void Constructor_NullServiceProvider_ThrowsArgumentNullException()
    {
        Action act = () => new InboxCleanupService(
            null!,
            Options.Create(new OutboxInboxOptions()),
            NullLogger<InboxCleanupService>.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("serviceProvider");
    }

    [Fact]
    public void Constructor_NullOptions_ThrowsArgumentNullException()
    {
        var sp = Substitute.For<IServiceProvider>();
        Action act = () => new InboxCleanupService(
            sp,
            null!,
            NullLogger<InboxCleanupService>.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var sp = Substitute.For<IServiceProvider>();
        Action act = () => new InboxCleanupService(
            sp,
            Options.Create(new OutboxInboxOptions()),
            null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task PerformCleanupAsync_WhenRepositoryExists_PurgesExpiredRecordsAndReturnsTrue()
    {
        var repo = Substitute.For<IIdempotencyRepository>();
        var serviceProvider = Substitute.For<IServiceProvider>();
        var serviceScope = Substitute.For<IServiceScope>();
        var serviceScopeFactory = Substitute.For<IServiceScopeFactory>();

        serviceProvider.GetService(typeof(IServiceScopeFactory)).Returns(serviceScopeFactory);
        serviceScopeFactory.CreateScope().Returns(serviceScope);
        serviceScope.ServiceProvider.GetService(typeof(IIdempotencyRepository)).Returns(repo);

        var options = new OutboxInboxOptions
        {
            CleanupInterval = TimeSpan.FromHours(1),
            DuplicateDetectionWindow = TimeSpan.FromHours(24),
            RetentionPeriod = TimeSpan.FromDays(7)
        };

        var fixedTime = new DateTimeOffset(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);
        var fakeTime = new Microsoft.Extensions.Time.Testing.FakeTimeProvider(fixedTime);

        var service = new InboxCleanupService(
            serviceProvider,
            Options.Create(options),
            NullLogger<InboxCleanupService>.Instance,
            fakeTime);

        var result = await service.PerformCleanupAsync(CancellationToken.None);

        result.Should().BeTrue();
        var expectedCutoff = fixedTime - TimeSpan.FromHours(24);
        await repo.Received(1).PurgeExpiredRecordsAsync(
            Arg.Is<DateTimeOffset>(d => d == expectedCutoff),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PerformCleanupAsync_WhenNoRepositoryRegistered_LogsDebugAndReturnsFalse()
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        var serviceScope = Substitute.For<IServiceScope>();
        var serviceScopeFactory = Substitute.For<IServiceScopeFactory>();

        serviceProvider.GetService(typeof(IServiceScopeFactory)).Returns(serviceScopeFactory);
        serviceScopeFactory.CreateScope().Returns(serviceScope);
        serviceScope.ServiceProvider.GetService(typeof(IIdempotencyRepository)).Returns(null);

        var options = new OutboxInboxOptions
        {
            CleanupInterval = TimeSpan.FromHours(1),
            DuplicateDetectionWindow = TimeSpan.FromHours(24)
        };

        var service = new InboxCleanupService(
            serviceProvider,
            Options.Create(options),
            NullLogger<InboxCleanupService>.Instance);

        var result = await service.PerformCleanupAsync(CancellationToken.None);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task PerformCleanupAsync_WhenRepositoryThrowsException_PropagatesException()
    {
        var repo = Substitute.For<IIdempotencyRepository>();
        repo.PurgeExpiredRecordsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask>(_ => throw new InvalidOperationException("Simulated DB transient error during purge"));

        var scope = Substitute.For<IServiceScope>();
        scope.ServiceProvider.GetService(typeof(IIdempotencyRepository)).Returns(repo);

        var scopeFactory = Substitute.For<IServiceScopeFactory>();
        scopeFactory.CreateScope().Returns(scope);

        var serviceProvider = Substitute.For<IServiceProvider>();
        serviceProvider.GetService(typeof(IServiceScopeFactory)).Returns(scopeFactory);

        var options = new OutboxInboxOptions
        {
            CleanupInterval = TimeSpan.FromHours(1),
            DuplicateDetectionWindow = TimeSpan.FromHours(24)
        };

        var service = new InboxCleanupService(
            serviceProvider,
            Options.Create(options),
            NullLogger<InboxCleanupService>.Instance);

        var act = () => service.PerformCleanupAsync(CancellationToken.None);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("Simulated DB transient error during purge");
    }

    [Fact]
    public async Task ExecuteAsync_WithFakeTimeProvider_DelaysBeforeExecutingCleanup()
    {
        var repo = Substitute.For<IIdempotencyRepository>();
        var callCount = 0;
        var purgedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        repo.PurgeExpiredRecordsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                Interlocked.Increment(ref callCount);
                purgedTcs.TrySetResult(true);
                return ValueTask.CompletedTask;
            });

        var services = new ServiceCollection();
        services.AddScoped(_ => repo);
        var sp = services.BuildServiceProvider();

        var options = new OutboxInboxOptions
        {
            CleanupInterval = TimeSpan.FromHours(1),
            DuplicateDetectionWindow = TimeSpan.FromHours(24)
        };

        var fakeTime = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var service = new InboxCleanupService(
            sp,
            Options.Create(options),
            NullLogger<InboxCleanupService>.Instance,
            fakeTime);

        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await Task.Delay(50);

        // Before advancing time, Task.Delay has not expired, so purge has NOT run.
        callCount.Should().Be(0);

        // Advance time to trigger cleanup.
        fakeTime.Advance(TimeSpan.FromHours(1));
        await purgedTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));

        callCount.Should().Be(1);
        await repo.Received(1).PurgeExpiredRecordsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());

        cts.Cancel();
        await service.StopAsync(CancellationToken.None);
        if (service.ExecuteTask != null)
        {
            await service.ExecuteTask;
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenOperationCanceledExceptionThrown_BreaksLoopAndExits()
    {
        var repo = Substitute.For<IIdempotencyRepository>();
        var callCount = 0;
        var canceledTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        repo.PurgeExpiredRecordsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask>(_ =>
            {
                Interlocked.Increment(ref callCount);
                canceledTcs.TrySetResult(true);
                throw new OperationCanceledException();
            });

        var services = new ServiceCollection();
        services.AddScoped(_ => repo);
        var sp = services.BuildServiceProvider();

        var options = new OutboxInboxOptions
        {
            CleanupInterval = TimeSpan.FromHours(1),
            DuplicateDetectionWindow = TimeSpan.FromHours(24)
        };

        var fakeTime = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var service = new InboxCleanupService(
            sp,
            Options.Create(options),
            NullLogger<InboxCleanupService>.Instance,
            fakeTime);

        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await Task.Delay(50);

        fakeTime.Advance(TimeSpan.FromHours(1));
        await canceledTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // When OperationCanceledException is thrown inside PerformCleanupAsync,
        // it must break the loop and complete ExecuteTask even while cts.IsCancellationRequested is false.
        if (service.ExecuteTask != null)
        {
            await service.ExecuteTask.WaitAsync(TimeSpan.FromSeconds(10));
            service.ExecuteTask.IsCompleted.Should().BeTrue();
        }

        callCount.Should().Be(1);
        await service.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task ExecuteAsync_WhenGenericExceptionThrown_LogsErrorAndContinuesLoop()
    {
        var repo = Substitute.For<IIdempotencyRepository>();
        var callCount = 0;
        var tcs1 = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var tcs2 = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        repo.PurgeExpiredRecordsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                var count = Interlocked.Increment(ref callCount);
                if (count == 1)
                {
                    tcs1.TrySetResult(true);
                    throw new InvalidOperationException("Simulated transient error");
                }
                tcs2.TrySetResult(true);
                return ValueTask.CompletedTask;
            });

        var services = new ServiceCollection();
        services.AddScoped(_ => repo);
        var sp = services.BuildServiceProvider();

        var options = new OutboxInboxOptions
        {
            CleanupInterval = TimeSpan.FromHours(1),
            DuplicateDetectionWindow = TimeSpan.FromHours(24)
        };

        var fakeTime = new Microsoft.Extensions.Time.Testing.FakeTimeProvider();
        var logger = Substitute.For<Microsoft.Extensions.Logging.ILogger<InboxCleanupService>>();

        var service = new InboxCleanupService(
            sp,
            Options.Create(options),
            logger,
            fakeTime);

        using var cts = new CancellationTokenSource();
        await service.StartAsync(cts.Token);
        await Task.Delay(50);

        callCount.Should().Be(0);

        // First interval -> throws ex -> caught and logged
        fakeTime.Advance(TimeSpan.FromHours(1));
        await tcs1.Task.WaitAsync(TimeSpan.FromSeconds(5));
        callCount.Should().Be(1);

        await Task.Delay(50);

        // Second interval -> succeeds
        fakeTime.Advance(TimeSpan.FromHours(1));
        await tcs2.Task.WaitAsync(TimeSpan.FromSeconds(5));
        callCount.Should().Be(2);

        logger.Received(1).Log(
            Microsoft.Extensions.Logging.LogLevel.Error,
            Arg.Any<Microsoft.Extensions.Logging.EventId>(),
            Arg.Is<object>(o => o.ToString()!.Contains("Error occurred during inbox cleanup")),
            Arg.Any<InvalidOperationException>(),
            Arg.Any<Func<object, Exception?, string>>());

        cts.Cancel();
        await service.StopAsync(CancellationToken.None);
        if (service.ExecuteTask != null)
        {
            await service.ExecuteTask;
        }
    }

    [Fact]
    public async Task ExecuteAsync_WhenCancelledImmediately_ExitsCleanly()
    {
        var serviceProvider = Substitute.For<IServiceProvider>();
        var options = new OutboxInboxOptions
        {
            CleanupInterval = TimeSpan.FromMilliseconds(10)
        };

        var service = new InboxCleanupService(
            serviceProvider,
            Options.Create(options),
            NullLogger<InboxCleanupService>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await service.StartAsync(cts.Token);
        await service.StopAsync(CancellationToken.None);

        cts.IsCancellationRequested.Should().BeTrue();
    }
}
