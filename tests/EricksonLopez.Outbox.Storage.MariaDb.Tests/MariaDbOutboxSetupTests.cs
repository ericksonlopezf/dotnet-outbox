// Copyright © Erickson Lopez. MIT License.
using System;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Hosting;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Storage.MariaDb;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MySqlConnector;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Storage.MariaDb.Tests;

public class MariaDbOutboxSetupTests
{
    [Fact]
    public void UseMariaDb_NullOptions_ThrowsArgumentNullException()
    {
        OutboxOptions options = null!;
        Action act = () => options.UseMariaDb(sp => new MySqlConnection());
        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void UseMariaDb_NullConnectionFactory_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        Action act = () => services.AddOutbox(options => options.UseMariaDb(null!));
        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionFactory");
    }

    [Fact]
    public void UseMariaDb_RegistersServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddOutbox(options => options.UseMariaDb(sp => new MySqlConnection()));

        var provider = services.BuildServiceProvider();
        var connFactory = provider.GetRequiredService<Func<System.Data.IDbConnection>>();
        connFactory.Should().NotBeNull();
        var conn = connFactory();
        conn.Should().NotBeNull();

        provider.GetRequiredService<IOutboxRepository>().Should().BeOfType<MariaDbOutboxRepository>();
        provider.GetRequiredService<IDeadLetterRepository>().Should().BeOfType<MariaDbDeadLetterRepository>();
        provider.GetRequiredService<IIdempotencyRepository>().Should().BeOfType<MariaDbIdempotencyRepository>();
    }
}
