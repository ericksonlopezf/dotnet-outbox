#pragma warning disable CA2012
#pragma warning disable SYSLIB0050
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;

using EricksonLopez.Outbox.Dispatcher;
using EricksonLopez.Outbox.Hosting;
using EricksonLopez.Outbox.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace EricksonLopez.Outbox.Tests;

public class OutboxHealthCheckTests
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1822")]
    private OutboxDispatcherBackgroundService CreateDispatcher(bool isRunning)
    {
        var dispatcher = (OutboxDispatcherBackgroundService)FormatterServices.GetUninitializedObject(typeof(OutboxDispatcherBackgroundService));
        var field = typeof(OutboxDispatcherBackgroundService).GetField("_isRunning", BindingFlags.NonPublic | BindingFlags.Instance);
        field!.SetValue(dispatcher, isRunning ? 1 : 0);
        return dispatcher;
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsHealthy_WhenNoDispatcherRegistered()
    {
        var services = new ServiceCollection();
        var sp = services.BuildServiceProvider();
        var repo = Substitute.For<IOutboxRepository>();
        var opts = Options.Create(new OutboxHealthCheckOptions());
        var check = new OutboxHealthCheck(sp, repo, opts);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("producer-only");
        result.Data["dispatcher_state"].Should().Be("not_configured");
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsUnhealthy_WhenDispatcherNotRunning()
    {
        var services = new ServiceCollection();
        var dispatcher = CreateDispatcher(false);
        services.AddSingleton(dispatcher);
        var sp = services.BuildServiceProvider();

        var repo = Substitute.For<IOutboxRepository>();
        var opts = Options.Create(new OutboxHealthCheckOptions());
        var check = new OutboxHealthCheck(sp, repo, opts);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("not running");
        result.Data["dispatcher_state"].Should().Be("stopped");
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsHealthy_WhenDispatcherRunning_AndUnderThreshold()
    {
        var services = new ServiceCollection();
        var dispatcher = CreateDispatcher(true);
        services.AddSingleton(dispatcher);
        var sp = services.BuildServiceProvider();

        var repo = Substitute.For<IOutboxRepository>();
        repo.GetPendingCountAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<long>(500));

        var opts = Options.Create(new OutboxHealthCheckOptions { WarningThreshold = 2000 });
        var check = new OutboxHealthCheck(sp, repo, opts);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Description.Should().Contain("500 pending messages");
        result.Data["dispatcher_state"].Should().Be("running");
        result.Data["pending_messages"].Should().Be(500);
        result.Data["warning_threshold"].Should().Be(2000);
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsDegraded_WhenDispatcherRunning_AndOverThreshold()
    {
        var services = new ServiceCollection();
        var dispatcher = CreateDispatcher(true);
        services.AddSingleton(dispatcher);
        var sp = services.BuildServiceProvider();

        var repo = Substitute.For<IOutboxRepository>();
        repo.GetPendingCountAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<long>(1500));

        var opts = Options.Create(new OutboxHealthCheckOptions { WarningThreshold = 1000 });
        var check = new OutboxHealthCheck(sp, repo, opts);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("1500 pending messages");
        result.Data["pending_messages"].Should().Be(1500);
    }
    
    [Fact]
    public async Task CheckHealthAsync_ReturnsDegraded_WhenDispatcherRunning_AndExactlyAtThreshold()
    {
        var services = new ServiceCollection();
        var dispatcher = CreateDispatcher(true);
        services.AddSingleton(dispatcher);
        var sp = services.BuildServiceProvider();
        var repo = Substitute.For<IOutboxRepository>();
        repo.GetPendingCountAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<long>(1000));
        var opts = Options.Create(new OutboxHealthCheckOptions { WarningThreshold = 1000 });
        var check = new OutboxHealthCheck(sp, repo, opts);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Description.Should().Contain("1000 pending messages");
        result.Data["pending_messages"].Should().Be(1000);
    }
    
    [Fact]
    public async Task Constructor_WithNullOptions_ShouldUseDefault()
    {
        var services = new ServiceCollection();
        var dispatcher = CreateDispatcher(true);
        services.AddSingleton(dispatcher);
        var sp = services.BuildServiceProvider();
        var repo = Substitute.For<IOutboxRepository>();
        repo.GetPendingCountAsync(Arg.Any<CancellationToken>()).Returns(ValueTask.FromResult<long>(1000));
        
        // Pass null options
        var check = new OutboxHealthCheck(sp, repo, null!);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        // Default WarningThreshold is 1000, so exactly at threshold = degraded
        result.Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public async Task CheckHealthAsync_ReturnsUnhealthy_WhenRepoThrows()
    {
        var services = new ServiceCollection();
        var dispatcher = CreateDispatcher(true);
        services.AddSingleton(dispatcher);
        var sp = services.BuildServiceProvider();

        var repo = Substitute.For<IOutboxRepository>();
        repo.GetPendingCountAsync(Arg.Any<CancellationToken>()).Returns<ValueTask<long>>(_ => throw new InvalidOperationException("DB failure"));

        var opts = Options.Create(new OutboxHealthCheckOptions());
        var check = new OutboxHealthCheck(sp, repo, opts);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
        result.Description.Should().Contain("Failed to retrieve outbox pending message count");
        result.Exception.Should().NotBeNull();
        result.Exception!.Message.Should().Be("DB failure");
        result.Data["error"].Should().Be("DB failure");
    }

    [Fact]
    public void AddOutbox_HealthCheckExtensions_ShouldRegisterHealthCheck()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>));
        var tags = new[] { "db" };
        services.AddHealthChecks().AddOutbox(name: "MyOutboxCheck", tags: tags);

        var provider = services.BuildServiceProvider();
        var healthCheckService = provider.GetService<HealthCheckService>();
        
        healthCheckService.Should().NotBeNull();

        var registrations = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value.Registrations;
        registrations.Should().Contain(r => r.Name == "MyOutboxCheck");
        
        var reg = registrations.Should().ContainSingle(r => r.Name == "MyOutboxCheck").Subject;
        reg.Tags.Should().Contain("db");
    }

    [Fact]
    public void AddOutbox_HealthCheckExtensions_ShouldConfigureOptions()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>));
        services.AddHealthChecks().AddOutbox(warningThreshold: 55);

        var provider = services.BuildServiceProvider();
        var opts = provider.GetRequiredService<IOptions<OutboxHealthCheckOptions>>().Value;
        
        opts.WarningThreshold.Should().Be(55);
    }
}


