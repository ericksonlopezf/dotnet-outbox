using System;
using System.Linq;
using AwesomeAssertions;

using EricksonLopez.Outbox.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace EricksonLopez.Outbox.Tests;

public class OutboxServiceCollectionExtensionsTests
{
    [Fact]
    public void AddOutbox_Should_Register_Services()
    {
        var services = new ServiceCollection();
        services.AddOutbox();

        var provider = services.BuildServiceProvider();

        services.Any(s => s.ServiceType == typeof(IHostedService)).Should().BeTrue();
    }

    [Fact]
    public void AddOutbox_Should_Register_Diagnostics()
    {
        var services = new ServiceCollection();
        services.AddOutbox();

        services.Any(s => s.ServiceType == typeof(EricksonLopez.Outbox.Diagnostics.OutboxMetrics)).Should().BeTrue();
        services.Any(s => s.ServiceType == typeof(EricksonLopez.Outbox.Diagnostics.IErrorSanitizer)).Should().BeTrue();
    }

    [Fact]
    public void AddOutboxDispatcher_Should_Register_Services()
    {
        var services = new ServiceCollection();
        services.AddOutboxDispatcher();

        services.Any(s => s.ServiceType == typeof(IHostedService)).Should().BeTrue();
    }

    [Fact]
    public void AddOutboxInbox_Should_Register_Services()
    {
        var services = new ServiceCollection();
        services.AddOutboxInbox();

        services.Any(s => s.ServiceType == typeof(IHostedService)).Should().BeTrue();
    }

    [Fact]
    public void AddOutboxDispatcher_With_Action_Should_Register()
    {
        var services = new ServiceCollection();
        services.AddOutboxDispatcher(options => options.BatchSize = 100);

        services.Any(s => s.ServiceType == typeof(IHostedService)).Should().BeTrue();
    }

    [Fact]
    public void AddOutboxInbox_With_Action_Should_Register()
    {
        var services = new ServiceCollection();
        services.AddOutboxInbox(options => options.CleanupInterval = TimeSpan.FromHours(2));

        services.Any(s => s.ServiceType == typeof(IHostedService)).Should().BeTrue();
    }
}


