// Copyright © Erickson Lopez. MIT License.
using System.Threading.Tasks;
using DotNet.Testcontainers.Builders;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace EricksonLopez.Outbox.Storage.PostgreSql.Tests;

public class PostgreSqlContainerFixture : IAsyncLifetime
{
    public PostgreSqlContainer Container { get; } = new PostgreSqlBuilder("postgres:15-alpine").Build();
    public NpgsqlDataSource DataSource { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await Container.StartAsync();
        DataSource = NpgsqlDataSource.Create(Container.GetConnectionString());
    }

    public async Task DisposeAsync()
    {
        if (DataSource != null) await DataSource.DisposeAsync();
        await Container.DisposeAsync();
    }
}
