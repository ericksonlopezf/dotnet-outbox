// Copyright © Erickson Lopez. MIT License.
using System;
using System.Linq;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Aspire;
using EricksonLopez.Outbox.Hosting;
using EricksonLopez.Outbox.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Aspire;

public class OutboxAspireExtensionsTests
{
    private static readonly string[] ExpectedTags = ["ready", "live", "outbox"];

    [Fact]
    public void AddOutbox_With_Null_Builder_Throws_ArgumentNullException()
    {
        var act = () => OutboxAspireExtensions.AddOutbox(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("builder");
    }

    [Fact]
    public void AddOutbox_With_Null_Configure_Registers_Outbox_And_HealthChecks()
    {
        var builder = Substitute.For<IHostApplicationBuilder>();
        var services = new ServiceCollection();
        services.AddLogging();
        builder.Services.Returns(services);

        var returned = builder.AddOutbox(null);
        returned.Should().BeSameAs(builder);

        var provider = services.BuildServiceProvider();
        var healthCheckOptions = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;

        var registration = healthCheckOptions.Registrations.FirstOrDefault(r => r.Name == "outbox");
        registration.Should().NotBeNull();
        registration!.FailureStatus.Should().Be(HealthStatus.Unhealthy);
        registration.Tags.Should().Contain(ExpectedTags);
    }

    [Fact]
    public void AddOutbox_With_Configure_Delegate_Executes_Configuration()
    {
        var builder = Substitute.For<IHostApplicationBuilder>();
        var services = new ServiceCollection();
        services.AddLogging();
        builder.Services.Returns(services);

        bool configureExecuted = false;
        var serializer = Substitute.For<IOutboxSerializer>();
        var typeResolver = Substitute.For<IOutboxMessageTypeResolver>();

        var returned = builder.AddOutbox(options =>
        {
            configureExecuted = true;
            options.UseSerializer(serializer);
            options.UseTypeResolver(typeResolver);
        });

        returned.Should().BeSameAs(builder);
        configureExecuted.Should().BeTrue();

        var provider = services.BuildServiceProvider();
        var registeredSerializer = provider.GetService<IOutboxSerializer>();
        registeredSerializer.Should().BeSameAs(serializer);

        var healthCheckOptions = provider.GetRequiredService<IOptions<HealthCheckServiceOptions>>().Value;
        var registration = healthCheckOptions.Registrations.FirstOrDefault(r => r.Name == "outbox");
        registration.Should().NotBeNull();
        registration!.FailureStatus.Should().Be(HealthStatus.Unhealthy);
        registration.Tags.Should().Contain(ExpectedTags);
    }
}
