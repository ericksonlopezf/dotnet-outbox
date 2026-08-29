// Copyright © Erickson Lopez. MIT License.
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.EntityFrameworkCore.Entities;
using EricksonLopez.Outbox.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EricksonLopez.Outbox.EntityFrameworkCore;

/// <summary>
/// Provides an Entity Framework Core implementation of <see cref="IIdempotencyRepository"/>.
/// </summary>
/// <typeparam name="TDbContext">The application's <see cref="DbContext"/> type that contains idempotency records.</typeparam>
public class EntityFrameworkCoreIdempotencyRepository<TDbContext> : IIdempotencyRepository
    where TDbContext : DbContext
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="EntityFrameworkCoreIdempotencyRepository{TDbContext}"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider to resolve scoped DbContext instances.</param>
    /// <exception cref="ArgumentNullException"><paramref name="serviceProvider"/> is <see langword="null"/>.</exception>
    public EntityFrameworkCoreIdempotencyRepository(IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc/>
    public async ValueTask<bool> TryInsertAsync(
        IdempotencyRecord record,
        EricksonLopez.Outbox.Persistence.IOutboxTransactionContext? transaction = default,
        CancellationToken cancellationToken = default)
    {
        var dbContext = _serviceProvider.GetRequiredService<TDbContext>();

        var existing = await dbContext.Set<IdempotencyRecordEntity>()
            .FindAsync(new object[] { record.MessageId, record.ConsumerId }, cancellationToken)
            .ConfigureAwait(false);

        if (existing != null)
        {
            return false;
        }

        var entity = IdempotencyRecordEntity.FromModel(record);
        dbContext.Set<IdempotencyRecordEntity>().Add(entity);
        return true;
    }

    /// <inheritdoc/>
    public async ValueTask PurgeExpiredRecordsAsync(
        DateTimeOffset olderThan,
        CancellationToken cancellationToken = default)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();

        var oldRecords = await dbContext.Set<IdempotencyRecordEntity>()
            .Where(r => r.ProcessedAt < olderThan)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (oldRecords.Count > 0)
        {
            dbContext.Set<IdempotencyRecordEntity>().RemoveRange(oldRecords);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}




