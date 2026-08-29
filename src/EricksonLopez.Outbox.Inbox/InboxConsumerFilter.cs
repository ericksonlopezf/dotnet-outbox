// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Persistence;
using Microsoft.Extensions.Logging;

namespace EricksonLopez.Outbox.Inbox;

/// <summary>
/// Provides consumer-side message deduplication backed by <see cref="IIdempotencyRepository"/>.
/// </summary>
public sealed class InboxConsumerFilter : IInboxConsumerFilter
{
    private readonly IIdempotencyRepository _idempotencyRepository;
    private readonly ILogger<InboxConsumerFilter> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="InboxConsumerFilter"/> class.
    /// </summary>
    /// <param name="idempotencyRepository">The repository used to record processed messages.</param>
    /// <param name="logger">The logger instance.</param>
    /// <exception cref="ArgumentNullException"><paramref name="idempotencyRepository"/> or <paramref name="logger"/> is <see langword="null"/>.</exception>
    public InboxConsumerFilter(
        IIdempotencyRepository idempotencyRepository,
        ILogger<InboxConsumerFilter> logger)
    {
        _idempotencyRepository = idempotencyRepository ?? throw new ArgumentNullException(nameof(idempotencyRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async ValueTask<bool> ExecuteIdempotentlyAsync(
        string messageId,
        string consumerName,
        Func<CancellationToken, ValueTask> handler,
        IOutboxTransactionContext? transaction = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messageId);
        ArgumentNullException.ThrowIfNull(consumerName);
        ArgumentNullException.ThrowIfNull(handler);

        var record = new IdempotencyRecord(messageId, consumerName, DateTimeOffset.UtcNow);

        var isNew = await _idempotencyRepository.TryInsertAsync(record, transaction, cancellationToken).ConfigureAwait(false);
        if (!isNew)
        {
            _logger.LogInformation("Duplicate message detected: Id={MessageId}, Consumer={ConsumerName}. Skipping processing.", messageId, consumerName);
            return false;
        }

        await handler(cancellationToken).ConfigureAwait(false);
        return true;
    }
}



