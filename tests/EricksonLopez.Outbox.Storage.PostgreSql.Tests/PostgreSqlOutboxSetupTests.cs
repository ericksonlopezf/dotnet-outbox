// Copyright © Erickson Lopez. MIT License.
using System;
using AwesomeAssertions;
using EricksonLopez.Outbox.Hosting;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Storage.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Storage.PostgreSql.Tests;

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

    [Fact]
    public void UsePostgreSql_NullParameters_ThrowsExceptions()
    {
        var options = new OutboxOptions(new ServiceCollection());
        var dataSource = NpgsqlDataSource.Create("Host=localhost;Username=test;Password=test");

        Action actNullOptionsFactory = () => ((OutboxOptions)null!).UsePostgreSql(sp => dataSource);
        actNullOptionsFactory.Should().Throw<ArgumentNullException>().WithParameterName("options");

        Action actNullFactory = () => options.UsePostgreSql((Func<IServiceProvider, NpgsqlDataSource>)null!);
        actNullFactory.Should().Throw<ArgumentNullException>().WithParameterName("dataSourceFactory");

        Action actNullOptionsConn = () => ((OutboxOptions)null!).UsePostgreSql("Host=localhost;Username=test;Password=test");
        actNullOptionsConn.Should().Throw<ArgumentNullException>().WithParameterName("options");

        Action actNullConn = () => options.UsePostgreSql((string)null!);
        actNullConn.Should().Throw<ArgumentNullException>().WithParameterName("connectionString");

        Action actEmptyConn = () => options.UsePostgreSql("   ");
        actEmptyConn.Should().Throw<ArgumentException>().WithParameterName("connectionString");

        Action actNullOptionsNotif = () => ((OutboxOptions)null!).UsePostgreSqlNotifications();
        actNullOptionsNotif.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }
}






