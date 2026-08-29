// Copyright © Erickson Lopez. MIT License.
using System;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Hosting;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Storage.Sqlite;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Storage.Sqlite.Tests;

public class SqliteOutboxSetupTests
{
    [Fact]
    public void UseSqlite_NullOptions_ThrowsArgumentNullException()
    {
        OutboxOptions options = null!;
        Action act = () => options.UseSqlite(sp => new SqliteConnection("Data Source=:memory:"));
        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void UseSqlite_NullConnectionFactory_ThrowsArgumentNullException()
    {
        var services = new ServiceCollection();
        Action act = () => services.AddOutbox(options => options.UseSqlite(null!));
        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionFactory");
    }

    [Fact]
    public void UseSqlite_RegistersServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        services.AddOutbox(options => options.UseSqlite(sp => new SqliteConnection("Data Source=:memory:")));
        
        var provider = services.BuildServiceProvider();
        var connFactory = provider.GetRequiredService<Func<System.Data.IDbConnection>>();
        connFactory.Should().NotBeNull();
        var conn = connFactory();
        conn.Should().NotBeNull();

        provider.GetRequiredService<IOutboxRepository>().Should().BeOfType<SqliteOutboxRepository>();
        provider.GetRequiredService<IDeadLetterRepository>().Should().BeOfType<SqliteDeadLetterRepository>();
        provider.GetRequiredService<IIdempotencyRepository>().Should().BeOfType<SqliteIdempotencyRepository>();
    }
}
