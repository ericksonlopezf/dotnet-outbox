// Copyright © Erickson Lopez. MIT License.
using System;
using EricksonLopez.Outbox.Persistence;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

namespace EricksonLopez.Outbox.Storage.MariaDb;

/// <summary>
/// Provides extension methods for configuring MariaDB outbox storage.
/// </summary>
public static class MariaDbOutboxSetup
{
    /// <summary>
    /// Configures the outbox to use MariaDB as the storage engine using the provided connection factory.
    /// </summary>
    /// <param name="options">The outbox options being configured.</param>
    /// <param name="connectionFactory">A factory delegate to provide a <see cref="MySqlConnection"/> based on the <see cref="IServiceProvider"/>.</param>
    /// <returns>The original <see cref="OutboxOptions"/> for method chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> or <paramref name="connectionFactory"/> is <see langword="null"/>.</exception>
    public static OutboxOptions UseMariaDb(this OutboxOptions options, Func<IServiceProvider, MySqlConnection> connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(connectionFactory);

        options.Configure(services =>
        {
            services.AddSingleton<Func<System.Data.IDbConnection>>(sp => () => connectionFactory(sp));
            services.AddSingleton<IOutboxRepository, MariaDbOutboxRepository>();
            services.AddSingleton<IDeadLetterRepository, MariaDbDeadLetterRepository>();
            services.AddSingleton<IIdempotencyRepository, MariaDbIdempotencyRepository>();
        });

        return options;
    }
}
