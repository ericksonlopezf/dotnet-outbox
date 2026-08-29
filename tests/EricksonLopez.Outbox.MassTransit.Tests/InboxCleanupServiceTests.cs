// Copyright © Erickson Lopez. MIT License.
#pragma warning disable CA2012
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

namespace EricksonLopez.Outbox.Tests.MassTransit;

public class InboxCleanupServiceTests
{
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
    public async Task ExecuteAsync_WhenEnabled_StartsAndStopsCleanly()
    {
        var repo = Substitute.For<IIdempotencyRepository>();
        var serviceProvider = Substitute.For<IServiceProvider>();
        var serviceScope = Substitute.For<IServiceScope>();
        var serviceScopeFactory = Substitute.For<IServiceScopeFactory>();

        serviceProvider.GetService(typeof(IServiceScopeFactory)).Returns(serviceScopeFactory);
        serviceScopeFactory.CreateScope().Returns(serviceScope);
        serviceScope.ServiceProvider.GetService(typeof(IIdempotencyRepository)).Returns(repo);

        var purgeTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        repo.PurgeExpiredRecordsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(_ => {
                purgeTcs.TrySetResult();
                return ValueTask.CompletedTask;
            });

        var options = new OutboxInboxOptions
        {
            CleanupInterval = TimeSpan.FromMilliseconds(50),
            DuplicateDetectionWindow = TimeSpan.FromHours(24)
        };

        var service = new InboxCleanupService(
            serviceProvider,
            Options.Create(options),
            NullLogger<InboxCleanupService>.Instance);

        using var cts = new CancellationTokenSource();
        var startTask = service.StartAsync(cts.Token);

        await purgeTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cts.Cancel();
        await service.StopAsync(CancellationToken.None);

        await repo.Received().PurgeExpiredRecordsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
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
    }
}




