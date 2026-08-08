using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using EricksonLopez.Outbox.Persistence;

namespace EricksonLopez.Outbox.EntityFrameworkCore;

/// <summary>
/// Provides extension methods for registering Entity Framework Core persistence services for the outbox.
/// </summary>
public static class OutboxEntityFrameworkCoreSetup
{
    /// <summary>
    /// Registers Entity Framework Core outbox persistence repositories for the specified <typeparamref name="TDbContext"/> type.
    /// </summary>
    /// <typeparam name="TDbContext">The application <see cref="DbContext"/> type that owns the outbox tables.</typeparam>
    /// <param name="services">The service collection to add the repositories to.</param>
    /// <returns>The modified <see cref="IServiceCollection"/> for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> is <see langword="null"/>.</exception>
    public static IServiceCollection AddOutboxEntityFrameworkCore<TDbContext>(
        this IServiceCollection services)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IOutboxRepository, EntityFrameworkCoreOutboxRepository<TDbContext>>();
        services.TryAddScoped<IIdempotencyRepository, EntityFrameworkCoreIdempotencyRepository<TDbContext>>();
        services.TryAddSingleton<IDeadLetterRepository, EntityFrameworkCoreDeadLetterRepository<TDbContext>>();

        // Register default IOutbox implementation
        services.TryAddScoped<IOutbox, DefaultOutbox>();

        return services;
    }
}
