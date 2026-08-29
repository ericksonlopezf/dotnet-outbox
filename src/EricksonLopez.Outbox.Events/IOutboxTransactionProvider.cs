// Copyright © Erickson Lopez. MIT License.
using System;
using EricksonLopez.Outbox.Persistence;

namespace EricksonLopez.Outbox.Events;

/// <summary>
/// Defines a provider that accesses the active <see cref="IOutboxTransactionContext"/> for outbox persistence.
/// </summary>
public interface IOutboxTransactionProvider
{
    /// <summary>
    /// Gets the current active transaction context, or <see langword="null"/> if no transaction is active.
    /// </summary>
    IOutboxTransactionContext? CurrentTransaction { get; }
}
