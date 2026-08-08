using EricksonLopez.Outbox.Hosting;
using System;

using AwesomeAssertions;

using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Storage.MySql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using MySqlConnector;

namespace EricksonLopez.Outbox.Tests;

public class MySqlOutboxSetupTests
{
    [Fact]
    public void UseMySql_RegistersServices()
    {
        var services = new ServiceCollection();
                services.AddLogging();
        services.AddOptions();
        services.AddOutbox(options => options.UseMySql(sp => Substitute.For<MySqlConnector.MySqlConnection>()));
        
        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<Func<System.Data.IDbConnection>>().Should().NotBeNull();
        provider.GetRequiredService<IOutboxRepository>().Should().BeOfType<MySqlOutboxRepository>();
    }
}





