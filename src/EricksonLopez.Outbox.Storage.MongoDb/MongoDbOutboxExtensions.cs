// Copyright © Erickson Lopez. MIT License.
using System;
using EricksonLopez.Outbox.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MongoDB.Driver;

namespace EricksonLopez.Outbox.Storage.MongoDb;

/// <summary>
/// Provides extension methods for registering MongoDB outbox storage.
/// </summary>
public static class MongoDbOutboxExtensions
{
    /// <summary>
    /// Registers MongoDB as the outbox storage and dead-letter queue repository.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="databaseFactory">Factory to resolve <see cref="IMongoDatabase"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> or <paramref name="databaseFactory"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddMongoDbOutbox(
        this IServiceCollection services,
        Func<IServiceProvider, IMongoDatabase> databaseFactory)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(databaseFactory);

        services.TryAddScoped(databaseFactory);
        services.AddScoped<IOutboxRepository, MongoDbOutboxRepository>();
        services.AddScoped<IDeadLetterRepository, MongoDbDeadLetterRepository>();

        return services;
    }
}
