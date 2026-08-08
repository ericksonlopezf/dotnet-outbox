#pragma warning disable CA2012
#pragma warning disable CA2254
using System;

using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;

using EricksonLopez.Outbox.Idempotency;
using EricksonLopez.Outbox.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Xunit;
using NSubstitute.ExceptionExtensions;
using Microsoft.Extensions.Logging;

namespace EricksonLopez.Outbox.Tests;

public class InboxCleanupServiceTests
{
    [Fact]
    public async Task ExecuteAsync_Should_Purge_Old_Records()
    {
        var services = new ServiceCollection();
        var repo = Substitute.For<IIdempotencyRepository>();
        services.AddScoped<IIdempotencyRepository>(_ => repo);
        var provider = services.BuildServiceProvider();

        var options = new OutboxInboxOptions
        {
            CleanupInterval = TimeSpan.FromMilliseconds(10)
        };

        var service = new InboxCleanupService(
            provider,
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<InboxCleanupService>.Instance);

        var cts = new CancellationTokenSource();
        var task = service.StartAsync(cts.Token);

        var timeout = DateTimeOffset.UtcNow.AddSeconds(5);
        while (DateTimeOffset.UtcNow < timeout)
        {
            var calls = repo.ReceivedCalls();
            if (System.Linq.Enumerable.Any(calls)) break;
            await Task.Delay(50);
        }

        cts.Cancel();
        try { await service.StopAsync(default); } catch { }

        await repo.Received().PurgeExpiredRecordsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_Should_Skip_If_No_Repo()
    {
        var services = new ServiceCollection();
        // No repo registered
        var provider = services.BuildServiceProvider();

        var options = new OutboxInboxOptions
        {
            CleanupInterval = TimeSpan.FromMilliseconds(10)
        };

        var service = new InboxCleanupService(
            provider,
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<InboxCleanupService>.Instance);

        var cts = new CancellationTokenSource();
        var task = service.StartAsync(cts.Token);

        await Task.Delay(500);
        cts.Cancel();

        try { await service.StopAsync(default); } catch { }
        
        // Should not throw
    }

    [Fact]
    public async Task ExecuteAsync_Should_Catch_And_Log_Exceptions()
    {
        var services = new ServiceCollection();
        var repo = Substitute.For<IIdempotencyRepository>();
        repo.PurgeExpiredRecordsAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromException(new InvalidOperationException("DB error")));
            
        services.AddScoped<IIdempotencyRepository>(_ => repo);
        var provider = services.BuildServiceProvider();

        var options = new OutboxInboxOptions
        {
            CleanupInterval = TimeSpan.FromMilliseconds(10)
        };

        var logger = Substitute.For<ILogger<InboxCleanupService>>();
        var service = new InboxCleanupService(
            provider,
            Microsoft.Extensions.Options.Options.Create(options),
            logger);

        var cts = new CancellationTokenSource();
        var task = service.StartAsync(cts.Token);

        await Task.Delay(500);
        cts.Cancel();

        try { await service.StopAsync(default); } catch { }

        logger.ReceivedWithAnyArgs().Log(
            LogLevel.Error,
            default,
            default,
            default,
            default!);
    }
}





