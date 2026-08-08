using System;

using EricksonLopez.Outbox.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace EricksonLopez.Outbox.Storage.PostgreSql;

/// <summary>
/// Dependency Injection setup extensions for PostgreSQL Outbox Storage.
/// </summary>
public static class PostgreSqlOutboxSetup
{
    /// <summary>
    /// Configures the outbox to use PostgreSQL as the storage engine using the provided data source factory.
    /// </summary>
    /// <param name="options">The outbox options being configured.</param>
    /// <param name="dataSourceFactory">A factory delegate to provide an <see cref="NpgsqlDataSource"/> based on the <see cref="IServiceProvider"/>.</param>
    /// <returns>The original <see cref="OutboxOptions"/> for method chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> or <paramref name="dataSourceFactory"/> is <see langword="null"/>.</exception>
    [CLSCompliant(false)]
    public static OutboxOptions UsePostgreSql(this OutboxOptions options, Func<IServiceProvider, NpgsqlDataSource> dataSourceFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(dataSourceFactory);

        options.Configure(services =>
        {
            services.AddSingleton(sp => dataSourceFactory(sp));
            services.AddSingleton<IOutboxRepository, PostgreSqlOutboxRepository>();
            services.AddSingleton<IDeadLetterRepository, PostgreSqlDeadLetterRepository>();
            services.AddSingleton<IIdempotencyRepository, PostgreSqlIdempotencyRepository>();
            // Register version validator to ensure PG >= 15
            services.AddHostedService<PostgreSqlVersionValidator>();
        });

        return options;
    }

    /// <summary>
    /// Configures the outbox to use PostgreSQL as the storage engine using the provided connection string.
    /// </summary>
    /// <param name="options">The outbox options being configured.</param>
    /// <param name="connectionString">The PostgreSQL connection string used to connect to the database.</param>
    /// <returns>The original <see cref="OutboxOptions"/> for method chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="connectionString"/> is <see langword="null"/> or whitespace.</exception>
    public static OutboxOptions UsePostgreSql(this OutboxOptions options, string connectionString)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        return options.UsePostgreSql(_ => NpgsqlDataSource.Create(connectionString));
    }

    /// <summary>
    /// Registers the <see cref="PostgresNotificationListener"/> background service to enable
    /// sub-millisecond wakeup latency via PostgreSQL <c>LISTEN</c>/<c>NOTIFY</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When registered, a dedicated background connection subscribes to the <c>outbox_new_messages</c>
    /// channel. Each INSERT into the outbox table fires a trigger that sends a <c>NOTIFY</c>, waking
    /// the dispatcher immediately instead of waiting for the next polling interval.
    /// </para>
    /// <para>
    /// <b>Prerequisites:</b> The SQL trigger defined in <c>01_Init_Outbox.sql</c> must be installed
    /// on the database. Execute <i>after</i> <see cref="UsePostgreSql(OutboxOptions, string)"/>.
    /// </para>
    /// <para>
    /// <b>Optional:</b> Not called automatically by <see cref="UsePostgreSql(OutboxOptions, string)"/>.
    /// If you do not need low-latency dispatch (e.g., your polling interval is already &lt; 1 second),
    /// you may omit this call to save one dedicated idle DB connection.
    /// </para>
    /// <example>
    /// <code>
    /// services.AddOutbox(options =>
    /// {
    ///     options
    ///         .UsePostgreSql(connectionString)
    ///         .UsePostgreSqlNotifications()  // optional — enables LISTEN/NOTIFY wakeup
    ///         .UseGeneratedTypes(MyJsonContext.Default);
    /// });
    /// </code>
    /// </example>
    /// </remarks>
    /// <param name="options">The outbox options being configured.</param>
    /// <returns>The original <see cref="OutboxOptions"/> for method chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> is <see langword="null"/>.</exception>
    public static OutboxOptions UsePostgreSqlNotifications(this OutboxOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.Configure(services =>
        {
            services.AddHostedService<PostgresNotificationListener>();
        });

        return options;
    }
}

