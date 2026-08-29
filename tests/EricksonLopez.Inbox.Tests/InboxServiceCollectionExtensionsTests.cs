// Copyright © Erickson Lopez. MIT License.
using System;
using AwesomeAssertions;
using EricksonLopez.Inbox.Configuration;
using EricksonLopez.Inbox.Core;
using EricksonLopez.Inbox.Hosting;
using EricksonLopez.Inbox.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Inbox.Tests;

public sealed class InboxServiceCollectionExtensionsTests
{
    [Fact]
    public void AddInbox_NullServices_ThrowsArgumentNullException()
    {
        Action act1 = () => ((IServiceCollection)null!).AddInbox();
        act1.Should().Throw<ArgumentNullException>().WithParameterName("services");

        Action act2 = () => ((IServiceCollection)null!).AddInbox(_ => { });
        act2.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddInMemoryInbox_NullServices_ThrowsArgumentNullException()
    {
        Action act1 = () => ((IServiceCollection)null!).AddInMemoryInbox();
        act1.Should().Throw<ArgumentNullException>().WithParameterName("services");

        Action act2 = () => ((IServiceCollection)null!).AddInMemoryInbox(_ => { });
        act2.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddInbox_WithoutConfigure_RegistersDefaultOptions_AndReturnsServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IInboxStore>());

        var result = services.AddInbox();
        result.Should().BeSameAs(services);

        using var provider = services.BuildServiceProvider();

        var filter = provider.GetService<IInboxConsumerFilter>();
        filter.Should().NotBeNull();
        filter.Should().BeOfType<DefaultInboxConsumerFilter>();

        var checker = provider.GetService<IIdempotencyChecker>();
        checker.Should().NotBeNull();
        checker.Should().BeOfType<IdempotencyChecker>();

        var options = provider.GetRequiredService<IOptions<InboxOptions>>().Value;
        options.RetentionPeriod.Should().Be(TimeSpan.FromDays(7));
        options.CleanupInterval.Should().Be(TimeSpan.FromHours(1));
        options.EnableAutomaticCleanup.Should().BeTrue();

        var hostedServices = provider.GetServices<IHostedService>();
        hostedServices.Should().Contain(s => s is InboxCleanupBackgroundService);
    }

    [Fact]
    public void AddInbox_WithConfigure_RegistersCustomOptions_AndReturnsServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IInboxStore>());

        var result = services.AddInbox(options =>
        {
            options.RetentionPeriod = TimeSpan.FromDays(21);
            options.CleanupInterval = TimeSpan.FromMinutes(45);
            options.EnableAutomaticCleanup = false;
        });
        result.Should().BeSameAs(services);

        using var provider = services.BuildServiceProvider();

        var filter = provider.GetService<IInboxConsumerFilter>();
        filter.Should().NotBeNull();
        filter.Should().BeOfType<DefaultInboxConsumerFilter>();

        var checker = provider.GetService<IIdempotencyChecker>();
        checker.Should().NotBeNull();
        checker.Should().BeOfType<IdempotencyChecker>();

        var options = provider.GetRequiredService<IOptions<InboxOptions>>().Value;
        options.RetentionPeriod.Should().Be(TimeSpan.FromDays(21));
        options.CleanupInterval.Should().Be(TimeSpan.FromMinutes(45));
        options.EnableAutomaticCleanup.Should().BeFalse();

        var hostedServices = provider.GetServices<IHostedService>();
        hostedServices.Should().Contain(s => s is InboxCleanupBackgroundService);
    }

    [Fact]
    public void AddInMemoryInbox_WithoutConfigure_RegistersInMemoryStoreAndDefaultOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var result = services.AddInMemoryInbox();
        result.Should().BeSameAs(services);

        using var provider = services.BuildServiceProvider();

        var store = provider.GetService<IInboxStore>();
        store.Should().NotBeNull();
        store.Should().BeOfType<InMemoryInboxStore>();

        var filter = provider.GetService<IInboxConsumerFilter>();
        filter.Should().NotBeNull();
        filter.Should().BeOfType<DefaultInboxConsumerFilter>();

        var checker = provider.GetService<IIdempotencyChecker>();
        checker.Should().NotBeNull();
        checker.Should().BeOfType<IdempotencyChecker>();

        var options = provider.GetRequiredService<IOptions<InboxOptions>>().Value;
        options.RetentionPeriod.Should().Be(TimeSpan.FromDays(7));
        options.CleanupInterval.Should().Be(TimeSpan.FromHours(1));
        options.EnableAutomaticCleanup.Should().BeTrue();

        var hostedServices = provider.GetServices<IHostedService>();
        hostedServices.Should().Contain(s => s is InboxCleanupBackgroundService);
    }

    [Fact]
    public void AddInMemoryInbox_WithConfigure_RegistersRequiredServices_AndCustomOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var result = services.AddInMemoryInbox(options =>
        {
            options.RetentionPeriod = TimeSpan.FromDays(14);
            options.CleanupInterval = TimeSpan.FromMinutes(30);
            options.EnableAutomaticCleanup = false;
        });
        result.Should().BeSameAs(services);

        using var provider = services.BuildServiceProvider();

        var store = provider.GetService<IInboxStore>();
        store.Should().NotBeNull();
        store.Should().BeOfType<InMemoryInboxStore>();

        var filter = provider.GetService<IInboxConsumerFilter>();
        filter.Should().NotBeNull();
        filter.Should().BeOfType<DefaultInboxConsumerFilter>();

        var checker = provider.GetService<IIdempotencyChecker>();
        checker.Should().NotBeNull();
        checker.Should().BeOfType<IdempotencyChecker>();

        var options = provider.GetRequiredService<IOptions<InboxOptions>>().Value;
        options.RetentionPeriod.Should().Be(TimeSpan.FromDays(14));
        options.CleanupInterval.Should().Be(TimeSpan.FromMinutes(30));
        options.EnableAutomaticCleanup.Should().BeFalse();

        var hostedServices = provider.GetServices<IHostedService>();
        hostedServices.Should().Contain(s => s is InboxCleanupBackgroundService);
    }
}
