// Copyright © Erickson Lopez. MIT License.
using System;
using EricksonLopez.Outbox.Persistence;

namespace EricksonLopez.Outbox.Events;

/// <summary>
/// Provides a no-op implementation of <see cref="IOutboxTransactionProvider"/> when no ambient transaction is managed.
/// </summary>
public sealed class NullOutboxTransactionProvider : IOutboxTransactionProvider
{
    /// <summary>
    /// Gets the singleton instance of <see cref="NullOutboxTransactionProvider"/>.
    /// </summary>
    public static NullOutboxTransactionProvider Instance { get; } = new();

    /// <inheritdoc/>
    public IOutboxTransactionContext? CurrentTransaction => null;
}
