// Copyright © Erickson Lopez. MIT License.
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using Testcontainers.Oracle;
using Xunit;

namespace EricksonLopez.Outbox.Storage.Oracle.Tests;

public class OracleContainerFixture : IAsyncLifetime
{
    public OracleContainer Container { get; } = new OracleBuilder("gvenzl/oracle-xe:21-slim-faststart")
        .WithPassword("password")
        .Build();

    public Task InitializeAsync() => Container.StartAsync();
    public Task DisposeAsync() => Container.DisposeAsync().AsTask();
}
