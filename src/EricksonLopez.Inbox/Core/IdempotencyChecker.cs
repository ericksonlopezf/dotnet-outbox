// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Inbox.Core;

/// <summary>
/// Provides the default implementation of <see cref="IIdempotencyChecker"/>.
/// </summary>
public sealed class IdempotencyChecker : IIdempotencyChecker
{
    private readonly IInboxStore _inboxStore;
    private readonly IInboxConsumerFilter _consumerFilter;

    /// <summary>
    /// Initializes a new instance of the <see cref="IdempotencyChecker"/> class.
    /// </summary>
    /// <param name="inboxStore">The inbox persistence store.</param>
    /// <param name="consumerFilter">The consumer filter.</param>
    /// <exception cref="ArgumentNullException"><paramref name="inboxStore"/> or <paramref name="consumerFilter"/> is <see langword="null"/>.</exception>
    public IdempotencyChecker(
        IInboxStore inboxStore,
        IInboxConsumerFilter consumerFilter)
    {
        _inboxStore = inboxStore ?? throw new ArgumentNullException(nameof(inboxStore));
        _consumerFilter = consumerFilter ?? throw new ArgumentNullException(nameof(consumerFilter));
    }

    /// <inheritdoc/>
    public ValueTask<bool> HasProcessedAsync(
        string messageId,
        string consumerName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messageId);
        ArgumentNullException.ThrowIfNull(consumerName);

        return _inboxStore.HasBeenProcessedAsync(messageId, consumerName, cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask<bool> ExecuteIdempotentlyAsync(
        string messageId,
        string consumerName,
        Func<CancellationToken, ValueTask> handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messageId);
        ArgumentNullException.ThrowIfNull(consumerName);
        ArgumentNullException.ThrowIfNull(handler);

        return _consumerFilter.ExecuteIdempotentlyAsync(messageId, consumerName, handler, cancellationToken);
    }
}
