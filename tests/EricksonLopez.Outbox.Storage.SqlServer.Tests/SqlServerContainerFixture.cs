// Copyright © Erickson Lopez. MIT License.
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using Testcontainers.MsSql;
using Xunit;

namespace EricksonLopez.Outbox.Storage.SqlServer.Tests;

public class SqlServerContainerFixture : IAsyncLifetime
{
    public MsSqlContainer Container { get; } = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    public Task InitializeAsync() => Container.StartAsync();
    public Task DisposeAsync() => Container.DisposeAsync().AsTask();
}
