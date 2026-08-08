using EricksonLopez.Outbox.Hosting;
using System;

using AwesomeAssertions;

using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Storage.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using Microsoft.Data.Sqlite;

namespace EricksonLopez.Outbox.Tests;

public class SqliteOutboxSetupTests
{
    [Fact]
    public void UseSqlite_RegistersServices()
    {
        var services = new ServiceCollection();
                services.AddLogging();
        services.AddOptions();
        services.AddOutbox(options => options.UseSqlite(sp => Substitute.For<Microsoft.Data.Sqlite.SqliteConnection>()));
        
        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<Func<System.Data.IDbConnection>>().Should().NotBeNull();
        provider.GetRequiredService<IOutboxRepository>().Should().BeOfType<SqliteOutboxRepository>();
    }
}





