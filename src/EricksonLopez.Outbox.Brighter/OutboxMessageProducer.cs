// Copyright © Erickson Lopez. MIT License.
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Persistence;
using Paramore.Brighter;

namespace EricksonLopez.Outbox.Brighter;

/// <summary>
/// Provides a message producer adapter for Paramore.Brighter that writes outgoing messages into <see cref="IOutbox"/>.
/// </summary>
public sealed class OutboxMessageProducer : IAmAMessageProducerAsync
{
    private readonly IOutbox _outbox;
    private readonly IOutboxTransactionContext? _transactionContext;
    private Publication _publication = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxMessageProducer"/> class.
    /// </summary>
    /// <param name="outbox">The outbox instance.</param>
    /// <param name="transactionContext">The active transaction context.</param>
    /// <exception cref="ArgumentNullException"><paramref name="outbox"/> is <see langword="null"/>.</exception>
    public OutboxMessageProducer(IOutbox outbox, IOutboxTransactionContext? transactionContext = null)
    {
        _outbox = outbox ?? throw new ArgumentNullException(nameof(outbox));
        _transactionContext = transactionContext;
    }

    /// <inheritdoc/>
    public IRequestContext? RequestContext { get; set; }

    /// <inheritdoc/>
    public Publication Publication
    {
        get => _publication;
        set => _publication = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <inheritdoc/>
    public Activity? Span { get; set; }

    /// <inheritdoc/>
    public IAmAMessageScheduler? Scheduler { get; set; }

    /// <inheritdoc/>
    public async Task SendAsync(Message message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (_transactionContext != null)
        {
            var body = message.Body?.Bytes;
            if (body != null && body.Length > 0)
            {
                await _outbox.StoreAsync(body, _transactionContext, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc/>
    public async Task SendWithDelayAsync(Message message, TimeSpan? delay = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (_transactionContext != null)
        {
            var body = message.Body?.Bytes;
            if (body != null && body.Length > 0)
            {
                var builder = _outbox.Publish(body)
                    .WithTransaction(_transactionContext);

                if (delay.HasValue && delay.Value > TimeSpan.Zero)
                {
                    builder.WithDelay(delay.Value);
                }

                await builder.StoreAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

#pragma warning disable CA1822
    /// <inheritdoc/>
    public void Dispose()
    {
    }
#pragma warning restore CA1822
}



