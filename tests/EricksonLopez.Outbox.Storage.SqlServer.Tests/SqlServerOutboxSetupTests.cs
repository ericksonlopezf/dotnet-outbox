// Copyright © Erickson Lopez. MIT License.
using System;
using System.Data;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Hosting;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Storage.SqlServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Storage.SqlServer.Tests;

public class SqlServerOutboxSetupTests
{
    [Fact]
    public void UseSqlServer_RegistersServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddOutbox(options => options.UseSqlServer(sp => Substitute.For<IDbConnection>()));
        
        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<Func<IDbConnection>>().Should().NotBeNull();
        provider.GetRequiredService<IOutboxRepository>().Should().BeOfType<SqlServerOutboxRepository>();
        provider.GetRequiredService<IIdempotencyRepository>().Should().BeOfType<SqlServerIdempotencyRepository>();
        provider.GetRequiredService<IDeadLetterRepository>().Should().BeOfType<SqlServerDeadLetterRepository>();
    }

    [Fact]
    public void UseSqlServer_NullOptions_ThrowsArgumentNullException()
    {
        OutboxOptions options = null!;
        Action act = () => options.UseSqlServer(sp => Substitute.For<IDbConnection>());
        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void UseSqlServer_NullConnectionFactory_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        Action act = () => services.AddOutbox(options => options.UseSqlServer(null!));
        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionFactory");
    }

    [Fact]
    public void UseSqlServer_DefaultSchemaAndTable_RewritesToDboAndOutboxMessages()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddOutbox(options => options.UseSqlServer(sp => Substitute.For<IDbConnection>()));

        var provider = services.BuildServiceProvider();
        var runtimeOptions = provider.GetRequiredService<IOptions<OutboxRuntimeOptions>>().Value;

        runtimeOptions.SchemaName.Should().Be("dbo");
        runtimeOptions.TableName.Should().Be("outbox_messages");
    }

    [Fact]
    public void UseSqlServer_CustomSchemaAndTable_PreservesCustomNames()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddOutbox(options =>
        {
            options.ConfigureRuntimeOptions(rt =>
            {
                rt.SchemaName = "custom_schema";
                rt.TableName = "custom_table";
            });
            options.UseSqlServer(sp => Substitute.For<IDbConnection>());
        });

        var provider = services.BuildServiceProvider();
        var runtimeOptions = provider.GetRequiredService<IOptions<OutboxRuntimeOptions>>().Value;

        runtimeOptions.SchemaName.Should().Be("custom_schema");
        runtimeOptions.TableName.Should().Be("custom_table");
    }
}
