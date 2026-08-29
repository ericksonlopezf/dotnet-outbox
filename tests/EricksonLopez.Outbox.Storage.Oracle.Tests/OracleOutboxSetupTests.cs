// Copyright © Erickson Lopez. MIT License.
using System;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Hosting;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Storage.Oracle;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oracle.ManagedDataAccess.Client;
using Xunit;

namespace EricksonLopez.Outbox.Storage.Oracle.Tests;

public class OracleOutboxSetupTests
{
    [Fact]
    public void UseOracle_NullOptions_ThrowsArgumentNullException()
    {
        OutboxOptions options = null!;
        Action act = () => options.UseOracle(sp => new OracleConnection());
        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void UseOracle_NullConnectionFactory_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        Action act = () => services.AddOutbox(options => options.UseOracle(null!));
        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionFactory");
    }

    [Fact]
    public void UseOracle_RegistersServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddOutbox(options => options.UseOracle(sp => new OracleConnection()));
        
        var provider = services.BuildServiceProvider();
        var connFactory = provider.GetRequiredService<Func<System.Data.IDbConnection>>();
        connFactory.Should().NotBeNull();
        var conn = connFactory();
        conn.Should().NotBeNull();

        provider.GetRequiredService<IOutboxRepository>().Should().BeOfType<OracleOutboxRepository>();
        provider.GetRequiredService<IDeadLetterRepository>().Should().BeOfType<OracleDeadLetterRepository>();
        provider.GetRequiredService<IIdempotencyRepository>().Should().BeOfType<OracleIdempotencyRepository>();
    }
}
