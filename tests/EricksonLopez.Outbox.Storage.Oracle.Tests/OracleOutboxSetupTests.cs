using EricksonLopez.Outbox.Hosting;
using System;

using AwesomeAssertions;

using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Storage.Oracle;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using Oracle.ManagedDataAccess.Client;

namespace EricksonLopez.Outbox.Tests;

public class OracleOutboxSetupTests
{
    [Fact]
    public void UseOracle_RegistersServices()
    {
        var services = new ServiceCollection();
                services.AddLogging();
        services.AddOptions();
        services.AddOutbox(options => options.UseOracle(sp => Substitute.For<Oracle.ManagedDataAccess.Client.OracleConnection>()));
        
        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<Func<System.Data.IDbConnection>>().Should().NotBeNull();
        provider.GetRequiredService<IOutboxRepository>().Should().BeOfType<OracleOutboxRepository>();
    }
}





