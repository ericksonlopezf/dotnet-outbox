using System;

using EricksonLopez.Outbox.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Oracle.ManagedDataAccess.Client;

namespace EricksonLopez.Outbox.Storage.Oracle;

/// <summary>
/// Dependency Injection setup extensions for Oracle Outbox Storage.
/// </summary>
public static class OracleOutboxSetup
{
    /// <summary>
    /// Configures the outbox to use Oracle as the storage engine using the provided connection factory.
    /// </summary>
    /// <param name="options">The outbox options being configured.</param>
    /// <param name="connectionFactory">A factory delegate to provide an <see cref="OracleConnection"/> based on the <see cref="IServiceProvider"/>.</param>
    /// <returns>The original <see cref="OutboxOptions"/> for method chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> or <paramref name="connectionFactory"/> is <see langword="null"/>.</exception>
    public static OutboxOptions UseOracle(this OutboxOptions options, Func<IServiceProvider, OracleConnection> connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(connectionFactory);

        options.Configure(services =>
        {
            services.AddSingleton<Func<System.Data.IDbConnection>>(sp => () => connectionFactory(sp));
            services.AddSingleton<IOutboxRepository, OracleOutboxRepository>();
            services.AddSingleton<IDeadLetterRepository, OracleDeadLetterRepository>();
            services.AddSingleton<IIdempotencyRepository, OracleIdempotencyRepository>();
        });

        return options;
    }
}
