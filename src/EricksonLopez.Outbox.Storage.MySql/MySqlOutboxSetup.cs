// Copyright © Erickson Lopez. MIT License.
using System;
using EricksonLopez.Outbox.Persistence;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

namespace EricksonLopez.Outbox.Storage.MySql;

/// <summary>
/// Provides extension methods for configuring MySQL outbox storage.
/// </summary>
public static class MySqlOutboxSetup
{
    /// <summary>
    /// Configures the outbox to use MySQL as the storage engine using the provided connection factory.
    /// </summary>
    /// <param name="options">The outbox options being configured.</param>
    /// <param name="connectionFactory">A factory delegate to provide a <see cref="MySqlConnection"/> based on the <see cref="IServiceProvider"/>.</param>
    /// <returns>The original <see cref="OutboxOptions"/> for method chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> or <paramref name="connectionFactory"/> is <see langword="null"/>.</exception>
    public static OutboxOptions UseMySql(this OutboxOptions options, Func<IServiceProvider, MySqlConnection> connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(connectionFactory);

        options.Configure(services =>
        {
            services.AddSingleton<Func<System.Data.IDbConnection>>(sp => () => connectionFactory(sp));
            services.AddSingleton<IOutboxRepository, MySqlOutboxRepository>();
            services.AddSingleton<IDeadLetterRepository, MySqlDeadLetterRepository>();
            services.AddSingleton<IIdempotencyRepository, MySqlIdempotencyRepository>();
        });

        return options;
    }
}

