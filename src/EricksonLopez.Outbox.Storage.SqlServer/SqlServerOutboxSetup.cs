using System;
using System.Data;

using EricksonLopez.Outbox.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace EricksonLopez.Outbox.Storage.SqlServer;

/// <summary>
/// Dependency Injection setup extensions for SQL Server Outbox Storage.
/// </summary>
public static class SqlServerOutboxSetup
{
    /// <summary>
    /// Configures the outbox to use SQL Server as the storage engine using the provided connection factory.
    /// </summary>
    /// <param name="options">The outbox options being configured.</param>
    /// <param name="connectionFactory">A factory delegate to provide an <see cref="IDbConnection"/> based on the <see cref="IServiceProvider"/>.</param>
    /// <returns>The original <see cref="OutboxOptions"/> for method chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="options"/> or <paramref name="connectionFactory"/> is <see langword="null"/>.</exception>
    public static OutboxOptions UseSqlServer(this OutboxOptions options, Func<IServiceProvider, IDbConnection> connectionFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(connectionFactory);

        // FIX-06: Use options.Configure() instead of options.Services directly.
        options.Configure(services =>
        {
            services.AddSingleton<Func<IDbConnection>>(sp => () => connectionFactory(sp));
            services.AddSingleton<IOutboxRepository, SqlServerOutboxRepository>();
            services.AddSingleton<IIdempotencyRepository, SqlServerIdempotencyRepository>();
            // Add SqlServerDeadLetterRepository as well, since it exists
            services.AddSingleton<IDeadLetterRepository, SqlServerDeadLetterRepository>();
        });

        options.ConfigureRuntimeOptions(runtime => 
        {
            if (runtime.SchemaName == "outbox")
            {
                runtime.SchemaName = "dbo";
            }
            if (runtime.TableName == "messages")
            {
                runtime.TableName = "outbox_messages";
            }
        });

        return options;
    }
}
