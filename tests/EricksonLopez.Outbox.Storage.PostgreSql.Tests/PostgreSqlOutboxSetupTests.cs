using EricksonLopez.Outbox.Hosting;
using System;

using AwesomeAssertions;

using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Storage.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;
using Npgsql;

namespace EricksonLopez.Outbox.Tests;

public class PostgreSqlOutboxSetupTests
{
    [Fact]
    public void UsePostgreSql_RegistersServices_WithFactory()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IBrokerPublisher>());
        var dataSource = NpgsqlDataSource.Create("Host=localhost;Username=test;Password=test");
        services.AddOutbox(options => options.UsePostgreSql(sp => dataSource));
        
        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<NpgsqlDataSource>().Should().NotBeNull();
        provider.GetRequiredService<IOutboxRepository>().Should().BeOfType<PostgreSqlOutboxRepository>();
        provider.GetRequiredService<IDeadLetterRepository>().Should().BeOfType<PostgreSqlDeadLetterRepository>();
        
        var hostedServices = provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>();
        hostedServices.Should().ContainSingle(s => s is PostgreSqlVersionValidator);
        
        provider.GetRequiredService<IIdempotencyRepository>().Should().BeOfType<PostgreSqlIdempotencyRepository>();
    }

    [Fact]
    public void UsePostgreSqlNotifications_RegistersListener()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IBrokerPublisher>());
        var dataSource = NpgsqlDataSource.Create("Host=localhost;Username=test;Password=test");
        services.AddOutbox(options => options.UsePostgreSql(sp => dataSource).UsePostgreSqlNotifications());
        
        var provider = services.BuildServiceProvider();
        var hostedServices = provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>();
        hostedServices.Should().ContainSingle(s => s is PostgresNotificationListener);
    }

    [Fact]
    public void UsePostgreSql_RegistersServices_ConnectionString()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<IBrokerPublisher>());
        services.AddOutbox(options => options.UsePostgreSql("Host=localhost;Username=test;Password=test"));
        
        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<NpgsqlDataSource>().Should().NotBeNull();
    }
}





