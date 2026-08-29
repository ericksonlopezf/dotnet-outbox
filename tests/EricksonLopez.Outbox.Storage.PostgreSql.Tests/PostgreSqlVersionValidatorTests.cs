// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Dapper;
using EricksonLopez.Outbox.Storage.PostgreSql;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace EricksonLopez.Outbox.Storage.PostgreSql.Tests;

[Trait("Category", "Integration")]
public class PostgreSqlVersionValidatorTests : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private NpgsqlDataSource? _dataSource;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder("postgres:15-alpine")
            .Build();
            
        await _container.StartAsync();
        _dataSource = NpgsqlDataSource.Create(_container.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        if (_dataSource != null) await _dataSource.DisposeAsync();
        if (_container != null) await _container.DisposeAsync();
    }

    [Fact]
    public async Task StartAsync_Should_Pass_For_Version_15()
    {
        var validator = new PostgreSqlVersionValidator(_dataSource!, NullLogger<PostgreSqlVersionValidator>.Instance);
        var act = () => validator.StartAsync(CancellationToken.None);
        
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StopAsync_Should_Not_Throw()
    {
        var validator = new PostgreSqlVersionValidator(_dataSource!, NullLogger<PostgreSqlVersionValidator>.Instance);
        var act = () => validator.StopAsync(CancellationToken.None);
        
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task StartAsync_Should_Throw_For_Version_14()
    {
        await using var pg14 = new PostgreSqlBuilder("postgres:14-alpine")
            .Build();
        
        await pg14.StartAsync();
        await using var ds14 = NpgsqlDataSource.Create(pg14.GetConnectionString());
        
        var validator = new PostgreSqlVersionValidator(ds14, NullLogger<PostgreSqlVersionValidator>.Instance);
        var act = () => validator.StartAsync(CancellationToken.None);
        
        var ex = await act.Should().ThrowAsync<NotSupportedException>();
        ex.WithMessage("*PostgreSQL 15 or higher*");
    }

    [Fact]
    public async Task StartAsync_Should_Catch_Other_Exceptions()
    {
        // Bad connection string will throw NpgsqlException or similar, which should be caught and not thrown
        await using var badDs = NpgsqlDataSource.Create("Host=localhost;Port=1234;Username=test;Password=test;Timeout=1");
        
        var validator = new PostgreSqlVersionValidator(badDs, NullLogger<PostgreSqlVersionValidator>.Instance);
        
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var act = () => validator.StartAsync(cts.Token);
        
        // It shouldn't throw NotSupportedException or anything, it should just swallow the exception.
        await act.Should().NotThrowAsync();
    }

    [Theory]
    [InlineData(150000, true)]
    [InlineData(150001, true)]
    [InlineData(160000, true)]
    [InlineData(149999, false)]
    [InlineData(140000, false)]
    public void ValidateServerVersion_BoundaryChecks(int versionNum, bool shouldPass)
    {
        var act = () => PostgreSqlVersionValidator.ValidateServerVersion(versionNum, NullLogger<PostgreSqlVersionValidator>.Instance);
        if (shouldPass)
        {
            act.Should().NotThrow();
        }
        else
        {
            var ex = act.Should().Throw<NotSupportedException>();
            ex.WithMessage("*PostgreSQL 15 or higher*");
        }
    }
}






