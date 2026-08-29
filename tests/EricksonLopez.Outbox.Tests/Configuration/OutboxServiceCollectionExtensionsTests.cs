// Copyright © Erickson Lopez. MIT License.
using System;
using System.Linq;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Diagnostics;
using EricksonLopez.Outbox.Dispatcher;
using EricksonLopez.Outbox.Hosting;
using EricksonLopez.Outbox.Idempotency;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Configuration;

public class OutboxServiceCollectionExtensionsTests
{
    [Fact]
    public void AddOutbox_Registers_All_Required_Descriptors_And_Validators()
    {
        var services = new ServiceCollection();
        services.AddOutbox();

        services.Any(s => s.ServiceType == typeof(IHostedService) && s.ImplementationType == typeof(OutboxStartupValidator)).Should().BeTrue();
        services.Any(s => s.ServiceType == typeof(IOutbox) && s.ImplementationType == typeof(DefaultOutbox)).Should().BeTrue();
        services.Any(s => s.ServiceType == typeof(IValidateOptions<OutboxDispatcherOptions>) && s.ImplementationType == typeof(OutboxDispatcherOptionsValidator)).Should().BeTrue();
        services.Any(s => s.ServiceType == typeof(IValidateOptions<OutboxRuntimeOptions>) && s.ImplementationType == typeof(OutboxRuntimeOptionsValidator)).Should().BeTrue();
        services.Any(s => s.ServiceType == typeof(global::EricksonLopez.Outbox.Diagnostics.OutboxMetrics)).Should().BeTrue();
        services.Any(s => s.ServiceType == typeof(global::EricksonLopez.Outbox.Diagnostics.IErrorSanitizer)).Should().BeTrue();

        var provider = services.BuildServiceProvider();
        var dispOpts = provider.GetRequiredService<IOptions<OutboxDispatcherOptions>>();
        dispOpts.Should().NotBeNull();
        var runOpts = provider.GetRequiredService<IOptions<OutboxRuntimeOptions>>();
        runOpts.Should().NotBeNull();
        provider.GetRequiredService<TimeProvider>().Should().BeSameAs(TimeProvider.System);
    }

    [Fact]
    public void AddOutbox_With_Publisher_And_Routes_Resolves_BrokerSelector()
    {
        var services = new ServiceCollection();
        var defaultPub = Substitute.For<IBrokerPublisher>();
        var routePub = Substitute.For<IBrokerPublisher>();

        services.AddOutbox(options =>
        {
            options.UseBroker(_ => defaultPub);
            options.Route("custom-route").ToPublisher(routePub);
        });

        var provider = services.BuildServiceProvider();

        var selector = provider.GetRequiredService<IBrokerSelector>();
        var msgUnknown = new OutboxMessage(Guid.NewGuid(), "unknown-route", default, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var msgCustom = new OutboxMessage(Guid.NewGuid(), "custom-route", default, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, 0, 0, null);
        selector.GetPublisher(msgUnknown).Should().BeSameAs(defaultPub);
        selector.GetPublisher(msgCustom).Should().BeSameAs(routePub);

        var pub = provider.GetRequiredService<IBrokerPublisher>();
        pub.Should().BeSameAs(defaultPub);
    }

    [Fact]
    public void AddOutbox_Without_DefaultPublisher_Resolves_BrokerSelector_With_Null_Default()
    {
        var services = new ServiceCollection();
        services.AddOutbox();

        var provider = services.BuildServiceProvider();

        var selector = provider.GetRequiredService<IBrokerSelector>();
        var msg = new OutboxMessage(Guid.NewGuid(), "any", default, null, null, ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null, 0, 0, null);
        var act = () => selector.GetPublisher(msg);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void AddOutboxDispatcher_WithNullAction_Registers_DefaultOptions_And_Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IBrokerPublisher>());
        services.AddSingleton(Substitute.For<global::EricksonLopez.Outbox.Persistence.IOutboxRepository>());
        services.AddSingleton(Substitute.For<global::EricksonLopez.Outbox.Serialization.IOutboxSerializer>());
        services.AddSingleton(Substitute.For<global::EricksonLopez.Outbox.Serialization.IOutboxMessageTypeResolver>());

        services.AddOutboxDispatcher();

        services.Any(s => s.ServiceType == typeof(IHostedService) && s.ImplementationType == typeof(OutboxDispatcherBackgroundService)).Should().BeTrue();
        services.Any(s => s.ServiceType == typeof(OutboxChannel) && s.ImplementationType == typeof(OutboxChannel)).Should().BeTrue();
        services.Any(s => s.ServiceType == typeof(AdaptivePoller) && s.ImplementationType == typeof(AdaptivePoller)).Should().BeTrue();
        services.Any(s => s.ServiceType == typeof(IValidateOptions<OutboxDispatcherOptions>) && s.ImplementationType == typeof(OutboxDispatcherOptionsValidator)).Should().BeTrue();

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OutboxDispatcherOptions>>().Value;
        options.BatchSize.Should().Be(100);

        var poller = provider.GetRequiredService<AdaptivePoller>();
        var wakeup = provider.GetRequiredService<IPollerWakeup>();
        wakeup.Should().BeSameAs(poller);
        provider.GetRequiredService<TimeProvider>().Should().BeSameAs(TimeProvider.System);
    }

    [Fact]
    public void AddOutboxDispatcher_WithAction_ConfiguresOptions_And_Registers_Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IBrokerPublisher>());
        services.AddSingleton(Substitute.For<global::EricksonLopez.Outbox.Persistence.IOutboxRepository>());
        services.AddSingleton(Substitute.For<global::EricksonLopez.Outbox.Serialization.IOutboxSerializer>());
        services.AddSingleton(Substitute.For<global::EricksonLopez.Outbox.Serialization.IOutboxMessageTypeResolver>());

        services.AddOutboxDispatcher(options =>
        {
            options.BatchSize = 42;
            options.MaxDegreeOfParallelism = 4;
        });

        services.Any(s => s.ServiceType == typeof(IHostedService) && s.ImplementationType == typeof(OutboxDispatcherBackgroundService)).Should().BeTrue();
        services.Any(s => s.ServiceType == typeof(OutboxChannel)).Should().BeTrue();
        services.Any(s => s.ServiceType == typeof(AdaptivePoller)).Should().BeTrue();
        services.Any(s => s.ServiceType == typeof(IValidateOptions<OutboxDispatcherOptions>) && s.ImplementationType == typeof(OutboxDispatcherOptionsValidator)).Should().BeTrue();

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OutboxDispatcherOptions>>().Value;
        options.BatchSize.Should().Be(42);
        options.MaxDegreeOfParallelism.Should().Be(4);
    }

    [Fact]
    public void AddOutboxDispatcher_StandaloneWithoutLogging_ResolvesOptions()
    {
        var services = new ServiceCollection();
        services.AddOutboxDispatcher();

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OutboxDispatcherOptions>>().Value;
        options.Should().NotBeNull();
        options.BatchSize.Should().Be(100);
    }

    [Fact]
    public void AddOutboxInbox_StandaloneWithoutLogging_ResolvesOptions()
    {
        var services = new ServiceCollection();
        services.AddOutboxInbox();

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OutboxInboxOptions>>().Value;
        options.Should().NotBeNull();
        options.RetentionPeriod.Should().Be(TimeSpan.FromDays(7));
    }

    [Fact]
    public void AddOutboxInbox_WithAction_ConfiguresOptions_And_Registers_Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => Substitute.For<global::EricksonLopez.Outbox.Persistence.IIdempotencyRepository>());

        services.AddOutboxInbox(options =>
        {
            options.RetentionPeriod = TimeSpan.FromDays(14);
            options.CleanupInterval = TimeSpan.FromHours(6);
        });

        services.Any(s => s.ServiceType == typeof(IHostedService) && s.ImplementationType == typeof(InboxCleanupService)).Should().BeTrue();
        services.Any(s => s.ServiceType == typeof(IInboxIdempotencyChecker) && s.ImplementationType == typeof(InboxIdempotencyChecker)).Should().BeTrue();

        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<OutboxInboxOptions>>().Value;
        options.RetentionPeriod.Should().Be(TimeSpan.FromDays(14));
        options.CleanupInterval.Should().Be(TimeSpan.FromHours(6));

        using var scope = provider.CreateScope();
        var checker = scope.ServiceProvider.GetRequiredService<IInboxIdempotencyChecker>();
        checker.Should().NotBeNull();
        checker.Should().BeOfType<InboxIdempotencyChecker>();
    }

    [Fact]
    public void AddOutbox_WhenDefaultPublisherFactorySetDirectly_RegistersPublisher()
    {
        var services = new ServiceCollection();
        var pub = Substitute.For<IBrokerPublisher>();
        services.AddOutbox(options =>
        {
            options.DefaultPublisherFactory = _ => pub;
        });

        services.Any(s => s.ServiceType == typeof(IBrokerPublisher)).Should().BeTrue();
        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<IBrokerPublisher>().Should().BeSameAs(pub);
    }

    [Fact]
    public void AddOutbox_ValidatesOutboxDispatcherOptions()
    {
        var services = new ServiceCollection();
        services.AddOutbox();
        services.Configure<OutboxDispatcherOptions>(o => o.BatchSize = -5);

        var provider = services.BuildServiceProvider();
        var act = () => _ = provider.GetRequiredService<IOptions<OutboxDispatcherOptions>>().Value;
        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void AddOutbox_ValidatesOutboxRuntimeOptions()
    {
        var services = new ServiceCollection();
        services.AddOutbox();
        services.Configure<OutboxRuntimeOptions>(o => o.SchemaName = "invalid!schema");

        var provider = services.BuildServiceProvider();
        var act = () => _ = provider.GetRequiredService<IOptions<OutboxRuntimeOptions>>().Value;
        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void AddOutboxDispatcher_ValidatesOutboxDispatcherOptions()
    {
        var services = new ServiceCollection();
        services.AddOutboxDispatcher(o => o.BatchSize = -5);

        var provider = services.BuildServiceProvider();
        var act = () => _ = provider.GetRequiredService<IOptions<OutboxDispatcherOptions>>().Value;
        act.Should().Throw<OptionsValidationException>();
    }

    [Fact]
    public void AddOutboxInbox_RegistersInboxCleanupHostedService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => Substitute.For<global::EricksonLopez.Outbox.Persistence.IIdempotencyRepository>());
        services.AddOutboxInbox();

        var provider = services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>();
        hostedServices.Should().ContainSingle(s => s is InboxCleanupService);
    }

    [Fact]
    public void AddOutboxDiagnostics_Helper_Is_Idempotent()
    {
        var services = new ServiceCollection();
        OutboxServiceCollectionInternals.AddOutboxDiagnostics(services);
        OutboxServiceCollectionInternals.AddOutboxDiagnostics(services);

        var provider = services.BuildServiceProvider();

        var metrics = provider.GetRequiredService<global::EricksonLopez.Outbox.Diagnostics.OutboxMetrics>();
        metrics.Should().NotBeNull();

        var sanitizer = provider.GetRequiredService<global::EricksonLopez.Outbox.Diagnostics.IErrorSanitizer>();
        sanitizer.Should().NotBeNull();
    }

    [Fact]
    public void AddOutboxCleanupService_WhenNullServices_ThrowsArgumentNullException()
    {
        Action act = () => OutboxHealthCheckExtensions.AddOutboxCleanupService(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddOutboxCleanupService_WithNullAction_DefaultsEnabledToTrue_AndRegistersHostedService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOutboxCleanupService();

        var provider = services.BuildServiceProvider();
        var opt = provider.GetRequiredService<IOptions<OutboxCleanupOptions>>().Value;
        opt.Enabled.Should().BeTrue();

        var hosted = provider.GetServices<IHostedService>();
        hosted.Should().ContainSingle(s => s is OutboxCleanupService);
    }

    [Fact]
    public void AddOutboxCleanupService_WithAction_ConfiguresOptions_AndRegistersHostedService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOutboxCleanupService(opt =>
        {
            opt.Enabled = false;
            opt.RetentionPeriod = TimeSpan.FromDays(30);
        });

        var provider = services.BuildServiceProvider();
        var opt = provider.GetRequiredService<IOptions<OutboxCleanupOptions>>().Value;
        opt.Enabled.Should().BeFalse();
        opt.RetentionPeriod.Should().Be(TimeSpan.FromDays(30));

        var hosted = provider.GetServices<IHostedService>();
        hosted.Should().ContainSingle(s => s is OutboxCleanupService);
    }
}

