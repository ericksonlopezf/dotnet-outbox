// Copyright © Erickson Lopez. MIT License.
using System;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Hosting;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Storage.MySql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Storage.MySql.Tests;

public class MySqlOutboxSetupTests
{
    [Fact]
    public void UseMySql_NullOptions_ThrowsArgumentNullException()
    {
        OutboxOptions options = null!;
        Action act = () => options.UseMySql(sp => new MySqlConnection());
        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void UseMySql_NullConnectionFactory_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        Action act = () => services.AddOutbox(options => options.UseMySql(null!));
        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionFactory");
    }

    [Fact]
    public void UseMySql_RegistersServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddOutbox(options => options.UseMySql(sp => new MySqlConnection()));
        
        var provider = services.BuildServiceProvider();
        var connFactory = provider.GetRequiredService<Func<System.Data.IDbConnection>>();
        connFactory.Should().NotBeNull();
        var conn = connFactory();
        conn.Should().NotBeNull();

        provider.GetRequiredService<IOutboxRepository>().Should().BeOfType<MySqlOutboxRepository>();
        provider.GetRequiredService<IDeadLetterRepository>().Should().BeOfType<MySqlDeadLetterRepository>();
        provider.GetRequiredService<IIdempotencyRepository>().Should().BeOfType<MySqlIdempotencyRepository>();
    }
}
