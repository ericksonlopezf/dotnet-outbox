// Copyright © Erickson Lopez. MIT License.
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Outbox.Persistence;

/// <summary>
/// Provides extension methods for <see cref="IOutboxRepository"/>.
/// </summary>
public static class OutboxRepositoryExtensions
{
    /// <summary>
    /// Marks a single message as failed.
    /// </summary>
    /// <param name="repository">The outbox repository to update.</param>
    /// <param name="message">The message that failed to process.</param>
    /// <param name="error">The error message or exception details describing the failure.</param>
    /// <param name="isDeadLetter"><see langword="true"/> to mark the message as permanently failed (dead letter); otherwise, <see langword="false"/>.</param>
    /// <param name="cancellationToken">A token that can be used to cancel the asynchronous operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static ValueTask MarkAsFailedAsync(
        this IOutboxRepository repository,
        OutboxMessage message,
        string error,
        bool isDeadLetter = false,
        CancellationToken cancellationToken = default)
    {
        return repository.MarkAsFailedAsync(
            new SingleOutboxMessageList(message),
            error,
            isDeadLetter,
            cancellationToken);
    }
}
