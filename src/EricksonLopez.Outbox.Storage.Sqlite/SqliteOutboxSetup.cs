using System;

using EricksonLopez.Outbox.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.Sqlite;

namespace EricksonLopez.Outbox.Storage.Sqlite;

/// <summary>
/// Dependency Injection setup extensions for SQLite Outbox Storage.
/// </summary>
public static class SqliteOutboxSetup
{
    /// <summary>
    /// Configures the outbox to use SQLite as the storage engine using the provided connection factory.
    /// </summary>
    /// <param name="options">The outbox options being configured.</param>
    /// <param name="connectionFactory">A factory delegate to provide a <see cref="SqliteConnection"/> based on the <see cref="IServiceProvider"/>.</param>
    /// <returns>The original <see cref="OutboxOptions"/> for method chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> or <paramref name="connectionFactory"/> is <see langword="null"/>.</exception>
    public static OutboxOptions UseSqlite(this OutboxOptions options, Func<IServiceProvider, SqliteConnection> connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(connectionFactory);

        options.Configure(services =>
        {
            services.AddSingleton<Func<System.Data.IDbConnection>>(sp => () => connectionFactory(sp));
            services.AddSingleton<IOutboxRepository, SqliteOutboxRepository>();
            services.AddSingleton<IDeadLetterRepository, SqliteDeadLetterRepository>();
            services.AddSingleton<IIdempotencyRepository, SqliteIdempotencyRepository>();
        });

        return options;
    }
}
