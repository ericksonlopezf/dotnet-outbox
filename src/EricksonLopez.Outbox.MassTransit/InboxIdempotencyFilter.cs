// Copyright © Erickson Lopez. MIT License.
using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Persistence;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;

namespace EricksonLopez.Outbox.MassTransit;

/// <summary>
/// Provides a MassTransit filter that enforces the Inbox idempotency pattern by detecting
/// and suppressing duplicate messages before they reach the business consumer.
/// </summary>
/// <typeparam name="T">The type of the message being consumed.</typeparam>
public sealed class InboxIdempotencyFilter<T> : IFilter<ConsumeContext<T>> where T : class
{
    /// <inheritdoc/>
    public async Task Send(ConsumeContext<T> context, IPipe<ConsumeContext<T>> next)
    {
        var messageId = context.MessageId?.ToString();
        var consumerId = context.ReceiveContext.InputAddress?.ToString() ?? "UnknownQueue";
        
        if (string.IsNullOrEmpty(messageId))
        {
            // Without MessageId we cannot ensure idempotency safely, let it pass
            await next.Send(context);
            return;
        }

        // Resolving the necessary dependencies for this particular Scope (Dependency Injection)
        // In a real MassTransit app, ScopedFilter can be used or the IServiceProvider can be resolved from the context
        if (!context.TryGetPayload(out IServiceProvider? serviceProvider) || serviceProvider == null)
        {
            // Fallback if there is no service provider
            await next.Send(context);
            return;
        }

        // We assume the user registered their repository and transactions
        var idempotencyRepo = serviceProvider.GetService<IIdempotencyRepository>();
        var transaction = serviceProvider.GetService<EricksonLopez.Outbox.Persistence.IOutboxTransactionContext>();

        if (idempotencyRepo == null || transaction == null)
        {
            // We cannot protect it if they are not configured, delegate.
            await next.Send(context);
            return;
        }

        var record = new IdempotencyRecord(messageId, consumerId, DateTimeOffset.UtcNow);

        // Attempt to insert
        bool isNew = await idempotencyRepo.TryInsertAsync(record, transaction, context.CancellationToken);

        if (!isNew)
        {
            // Duplicate detected! 
            // Short-circuit. MassTransit will mark the message as ACK without executing the business Consumer.
            return;
        }

        // If it's new, continue the pipeline to the business Consumer
        await next.Send(context);
    }

    /// <summary>
    /// Probes the filter to expose its configuration in the MassTransit topology graph.
    /// </summary>
    /// <param name="context">The probe context that inspects the filter pipeline.</param>
    public void Probe(ProbeContext context)
    {
        context.CreateFilterScope("ericksonlopez-outbox-idempotency");
    }
}






