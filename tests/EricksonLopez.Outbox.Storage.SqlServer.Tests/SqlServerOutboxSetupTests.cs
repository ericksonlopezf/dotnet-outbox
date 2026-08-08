using EricksonLopez.Outbox.Hosting;
using System;
using System.Data;
using AwesomeAssertions;

using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Storage.SqlServer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Tests;

public class SqlServerOutboxSetupTests
{
    [Fact]
    public void UseSqlServer_RegistersServices()
    {
        var services = new ServiceCollection();
                services.AddLogging();
        services.AddOptions();
        services.AddOutbox(options => options.UseSqlServer(sp => Substitute.For<System.Data.IDbConnection>()));
        
        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<Func<IDbConnection>>().Should().NotBeNull();
        provider.GetRequiredService<IOutboxRepository>().Should().BeOfType<SqlServerOutboxRepository>();
        provider.GetRequiredService<IIdempotencyRepository>().Should().BeOfType<SqlServerIdempotencyRepository>();
        provider.GetRequiredService<IDeadLetterRepository>().Should().BeOfType<SqlServerDeadLetterRepository>();
    }
}





