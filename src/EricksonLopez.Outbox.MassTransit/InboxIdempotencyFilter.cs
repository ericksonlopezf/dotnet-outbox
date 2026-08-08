using System;
using System.Data;
using System.Threading.Tasks;
using MassTransit;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace EricksonLopez.Outbox.MassTransit;

/// <summary>
/// Represents a MassTransit filter that enforces the Inbox idempotency pattern by detecting
/// and suppressing duplicate messages before they reach the business consumer.
/// </summary>
public sealed class InboxIdempotencyFilter<T> : IFilter<ConsumeContext<T>> where T : class
{
    /// <summary>
    /// Performs idempotency checks and short-circuits the pipeline if a duplicate message is detected,
    /// forwarding unique messages to the next filter in the chain.
    /// </summary>
    /// <param name="context">The MassTransit consume context for the current message.</param>
    /// <param name="next">The next pipe in the filter chain, usually leading to the consumer.</param>
    /// <returns>A task that represents the asynchronous filtering operation.</returns>
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


