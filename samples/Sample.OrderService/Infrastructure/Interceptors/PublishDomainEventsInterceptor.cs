// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Sample.OrderService.Infrastructure;
using Sample.OrderService.Shared;

namespace Sample.OrderService.Infrastructure.Interceptors;

public sealed class PublishDomainEventsInterceptor : SaveChangesInterceptor
{
    private readonly IOutbox _outbox;

    public PublishDomainEventsInterceptor(IOutbox outbox)
    {
        _outbox = outbox;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, 
        InterceptionResult<int> result, 
        CancellationToken cancellationToken = default)
    {
        var dbContext = eventData.Context;
        if (dbContext is null) return await base.SavingChangesAsync(eventData, result, cancellationToken);

        // Get all modified entities that are AggregateRoot and have domain events
        var entities = dbContext.ChangeTracker
            .Entries<AggregateRoot>()
            .Where(x => x.Entity.DomainEvents.Count > 0)
            .ToList();

        // Extract and clear the events to avoid processing them twice
        var domainEvents = entities
            .SelectMany(x => x.Entity.DomainEvents)
            .ToList();

        entities.ForEach(x => x.Entity.ClearDomainEvents());

        foreach (var domainEvent in domainEvents)
        {
            var transaction = dbContext.Database.CurrentTransaction?.GetDbTransaction();
            
            if (transaction != null)
            {
                await _outbox.StoreAsync(domainEvent, transaction.ToOutboxContext(), cancellationToken);
            }
            else
            {
                // If there is no transaction, force start one, or use the helper:
                await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken);
                await _outbox.StoreAsync(domainEvent, tx.GetDbTransaction().ToOutboxContext(), cancellationToken);
                await tx.CommitAsync(cancellationToken);
            }
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }
}





