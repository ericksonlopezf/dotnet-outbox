// Copyright © Erickson Lopez. MIT License.
using System.Threading.Tasks;
using Testcontainers.MariaDb;
using Xunit;

namespace EricksonLopez.Outbox.Storage.MariaDb.Tests;

public class MariaDbContainerFixture : IAsyncLifetime
{
    public MariaDbContainer Container { get; } = new MariaDbBuilder("mariadb:10.11")
        .WithDatabase("testdb")
        .WithUsername("root")
        .WithPassword("password")
        .Build();

    public Task InitializeAsync() => Container.StartAsync();
    public Task DisposeAsync() => Container.DisposeAsync().AsTask();
}
