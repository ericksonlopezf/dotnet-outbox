// Copyright © Erickson Lopez. MIT License.
using System;
using EricksonLopez.Outbox.Diagnostics;
using EricksonLopez.Outbox.Dispatcher;
using EricksonLopez.Outbox.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;

namespace EricksonLopez.Outbox.Tests.Infrastructure;

/// <summary>
/// Reusable test harness that encapsulates common Arrange plumbing for Poller and Channel tests.
/// Maintains test isolation by providing clean dependency injection scopes and test doubles per instance.
/// </summary>
internal sealed class TestDispatcherHarness : IDisposable
{
    /// <summary>
    /// Gets the service collection used to build the scoped service provider for the harness.
    /// Additional test-specific services can be registered here prior to building the provider.
    /// </summary>
    public ServiceCollection Services { get; } = new();

    /// <summary>
    /// Gets the built service provider. Initialized on first call to <see cref="BuildProvider"/>, <see cref="CreateChannel"/>, or <see cref="CreatePoller"/>.
    /// </summary>
    public ServiceProvider? Provider { get; private set; }

    /// <summary>
    /// Gets or sets the mock <see cref="IBrokerPublisher"/> instance. Initialized to a substitute by default.
    /// </summary>
    public IBrokerPublisher Publisher { get; set; } = Substitute.For<IBrokerPublisher>();

    /// <summary>
    /// Gets or sets the mock <see cref="IOutboxRepository"/> instance. Registered as a scoped service in DI.
    /// </summary>
    public IOutboxRepository Repository { get; set; } = Substitute.For<IOutboxRepository>();

    /// <summary>
    /// Gets or sets the mock <see cref="IDeadLetterRepository"/> instance. Registered as a scoped service in DI.
    /// </summary>
    public IDeadLetterRepository DeadLetterRepository { get; set; } = Substitute.For<IDeadLetterRepository>();

    /// <summary>
    /// Gets or sets the <see cref="IErrorSanitizer"/> instance. Initialized to a substitute by default.
    /// </summary>
    public IErrorSanitizer ErrorSanitizer { get; set; } = Substitute.For<IErrorSanitizer>();

    /// <summary>
    /// Gets the <see cref="OutboxMetrics"/> instance created for this harness.
    /// </summary>
    public OutboxMetrics Metrics { get; } = new();

    /// <summary>
    /// Gets the dispatcher options to be used when creating the channel and poller.
    /// Modify properties on this instance prior to creating the channel or poller.
    /// </summary>
    public OutboxDispatcherOptions DispatcherOptions { get; } = new()
    {
        UseAdaptivePolling = false,
        PollingInterval = TimeSpan.FromMilliseconds(10),
        BatchSize = 10
    };

    /// <summary>
    /// Gets the runtime options to be used when creating the channel and poller.
    /// Modify properties on this instance prior to creating the channel or poller.
    /// </summary>
    public OutboxRuntimeOptions RuntimeOptions { get; } = new();

    /// <summary>
    /// Builds the internal <see cref="ServiceProvider"/>, registering the configured <see cref="Repository"/> and <see cref="DeadLetterRepository"/> as scoped services.
    /// </summary>
    public ServiceProvider BuildProvider()
    {
        Services.AddScoped(_ => Repository);
        Services.AddScoped(_ => DeadLetterRepository);
        Provider = Services.BuildServiceProvider();
        return Provider;
    }

    /// <summary>
    /// Creates an <see cref="OutboxChannel"/> using the harness configuration and services.
    /// Automatically builds the <see cref="Provider"/> if not already built.
    /// </summary>
    public OutboxChannel CreateChannel()
    {
        if (Provider == null) BuildProvider();
        return new OutboxChannel(
            NullLogger<OutboxChannel>.Instance,
            Publisher,
            Options.Create(DispatcherOptions),
            Options.Create(RuntimeOptions),
            Metrics,
            Provider!.GetRequiredService<IServiceScopeFactory>(),
            ErrorSanitizer,
            TimeProvider.System);
    }

    /// <summary>
    /// Creates an <see cref="AdaptivePoller"/> using the harness configuration and services.
    /// Automatically builds the <see cref="Provider"/> if not already built.
    /// </summary>
    /// <param name="channel">The target channel for the poller.</param>
    /// <param name="timeProvider">Optional time provider. Defaults to <see cref="TimeProvider.System"/>.</param>
    public AdaptivePoller CreatePoller(OutboxChannel channel, TimeProvider? timeProvider = null)
    {
        if (Provider == null) BuildProvider();
        return new AdaptivePoller(
            Provider!,
            channel,
            Options.Create(DispatcherOptions),
            NullLogger<AdaptivePoller>.Instance,
            Metrics,
            timeProvider ?? TimeProvider.System);
    }

    /// <summary>
    /// Disposes the underlying <see cref="Provider"/> and releases resources.
    /// </summary>
    public void Dispose()
    {
        Provider?.Dispose();
    }
}
