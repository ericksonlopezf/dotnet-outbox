// Copyright © Erickson Lopez. MIT License.
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using Testcontainers.MySql;
using Xunit;

namespace EricksonLopez.Outbox.Storage.MySql.Tests;

public class MySqlContainerFixture : IAsyncLifetime
{
    public MySqlContainer Container { get; } = new MySqlBuilder("mysql:8.0").WithDatabase("testdb").WithUsername("root").WithPassword("password").WithCommand("--local-infile=1").Build();

    public Task InitializeAsync() => Container.StartAsync();
    public Task DisposeAsync() => Container.DisposeAsync().AsTask();
}
