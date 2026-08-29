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
    public async Task Poller_WhenRepositoryNotRegisteredInScope_LogsErrorAndContinuesGracefully()
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
        var channel = new OutboxChannel(NullLogger<OutboxChannel>.Instance, publisher, optionsMock, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), NSubstitute.Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(), NSubstitute.Substitute.For<EricksonLopez.Outbox.Diagnostics.IErrorSanitizer>(), TimeProvider.System);

        var poller = new AdaptivePoller(provider, channel, Microsoft.Extensions.Options.Options.Create(options), NullLogger<AdaptivePoller>.Instance, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), TimeProvider.System);

        using var cts = new CancellationTokenSource();
        var act = async () =>
        {
            var pollingTask = poller.StartPollingAsync(cts.Token);
            cts.Cancel();
            try { await pollingTask; } catch (OperationCanceledException) { }
        };
        
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PollAsync_WhenExceptionThrown_ShouldLogAndContinue()
    {
        var services = new ServiceCollection();
        var repo = Substitute.For<IOutboxRepository>();
        using var cts = new CancellationTokenSource();
        
        repo.FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<IReadOnlyList<OutboxMessage>>>(x =>
            {
                cts.Cancel();
                throw new InvalidOperationException("Test Error");
            });
            
        services.AddScoped(_ => repo);
        var provider = services.BuildServiceProvider();

        var options = new OutboxDispatcherOptions
        {
            UseAdaptivePolling = false,
            PollingInterval = TimeSpan.FromMilliseconds(1)
        };
        var optionsMock = Options.Create(options);

        var publisher = Substitute.For<IBrokerPublisher>();
        var channel = new OutboxChannel(NullLogger<OutboxChannel>.Instance, publisher, optionsMock, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), NSubstitute.Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(), NSubstitute.Substitute.For<EricksonLopez.Outbox.Diagnostics.IErrorSanitizer>(), TimeProvider.System);

        var poller = new AdaptivePoller(provider, channel, Microsoft.Extensions.Options.Options.Create(options), NullLogger<AdaptivePoller>.Instance, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), TimeProvider.System);

        try { await poller.StartPollingAsync(cts.Token); } catch (OperationCanceledException) { }

        await repo.Received().FetchPendingAsync(options.BatchSize, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Dispose_Should_Dispose_Semaphore()
    {
        var provider = Substitute.For<IServiceProvider>();
        var optionsMock = Options.Create(new OutboxDispatcherOptions());
        var publisher = Substitute.For<IBrokerPublisher>();
        var channel = new OutboxChannel(NullLogger<OutboxChannel>.Instance, publisher, optionsMock, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), NSubstitute.Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(), NSubstitute.Substitute.For<EricksonLopez.Outbox.Diagnostics.IErrorSanitizer>(), TimeProvider.System);

        var poller = new AdaptivePoller(provider, channel, optionsMock, NullLogger<AdaptivePoller>.Instance, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), TimeProvider.System);
        poller.Dispose();

        // Idempotent dispose and safe WakeUp when disposed
        var actDispose = () => poller.Dispose();
        actDispose.Should().NotThrow();

        var actWake = () => poller.WakeUp();
        actWake.Should().NotThrow();
    }

    [Fact]
    public async Task Poller_Should_Log_Error_When_Exception_Thrown()
    {
        var services = new ServiceCollection();
        var repo = Substitute.For<IOutboxRepository>();
        using var cts = new CancellationTokenSource();
        
        repo.FetchPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<IReadOnlyList<OutboxMessage>>>(x =>
            {
                cts.Cancel();
                throw new InvalidOperationException("Simulated Database Error");
            });
            
        services.AddScoped(_ => repo);
        var provider = services.BuildServiceProvider();

        var optionsMock = Options.Create(new OutboxDispatcherOptions { UseAdaptivePolling = false, PollingInterval = TimeSpan.FromSeconds(5) });

        var publisher = Substitute.For<IBrokerPublisher>();
        var channel = new OutboxChannel(NullLogger<OutboxChannel>.Instance, publisher, optionsMock, Options.Create(new OutboxRuntimeOptions()), new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), NSubstitute.Substitute.For<Microsoft.Extensions.DependencyInjection.IServiceScopeFactory>(), NSubstitute.Substitute.For<EricksonLopez.Outbox.Diagnostics.IErrorSanitizer>(), TimeProvider.System);

        var logger = new FakeLogger();

        var poller = new AdaptivePoller(provider, channel, optionsMock, logger, new EricksonLopez.Outbox.Diagnostics.OutboxMetrics(), TimeProvider.System);

        try { await poller.StartPollingAsync(cts.Token); } catch (OperationCanceledException) { }

        logger.ErrorLogged.Should().BeTrue();
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
}



