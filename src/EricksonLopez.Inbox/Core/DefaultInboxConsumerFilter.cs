// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace EricksonLopez.Inbox.Core;

/// <summary>
/// Provides the default implementation of <see cref="IInboxConsumerFilter"/> backed by an <see cref="IInboxStore"/>.
/// </summary>
public sealed class DefaultInboxConsumerFilter : IInboxConsumerFilter
{
    private readonly IInboxStore _inboxStore;
    private readonly ILogger<DefaultInboxConsumerFilter> _logger;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="DefaultInboxConsumerFilter"/> class.
    /// </summary>
    /// <param name="inboxStore">The underlying inbox store.</param>
    /// <param name="logger">Optional logger instance.</param>
    /// <param name="timeProvider">Optional time provider.</param>
    /// <exception cref="ArgumentNullException"><paramref name="inboxStore"/> is <see langword="null"/>.</exception>
    public DefaultInboxConsumerFilter(
        IInboxStore inboxStore,
        ILogger<DefaultInboxConsumerFilter>? logger = null,
        TimeProvider? timeProvider = null)
    {
        _inboxStore = inboxStore ?? throw new ArgumentNullException(nameof(inboxStore));
        _logger = logger ?? NullLogger<DefaultInboxConsumerFilter>.Instance;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc/>
    public async ValueTask<bool> ExecuteIdempotentlyAsync(
        string messageId,
        string consumerName,
        Func<CancellationToken, ValueTask> handler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messageId);
        ArgumentNullException.ThrowIfNull(consumerName);
        ArgumentNullException.ThrowIfNull(handler);

        var entry = new InboxEntry(messageId, consumerName, _timeProvider.GetUtcNow());
        var isNew = await _inboxStore.TryRecordAsync(entry, cancellationToken).ConfigureAwait(false);

        if (!isNew)
        {
            _logger.LogInformation(
                "Duplicate message detected in inbox: MessageId='{MessageId}', Consumer='{ConsumerName}'. Skipping execution.",
                messageId,
                consumerName);
            return false;
        }

        await handler(cancellationToken).ConfigureAwait(false);
        return true;
    }
}
