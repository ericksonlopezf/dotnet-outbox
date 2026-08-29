// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Outbox.Persistence;

/// <summary>
/// Defines a generic transaction context for the Outbox, decoupling operations from specific database transaction types.
/// </summary>
public interface IOutboxTransactionContext
{
    /// <summary>Gets the underlying transaction object as an untyped reference.</summary>
    object Transaction { get; }

    /// <summary>Gets the underlying connection object, or <see langword="null"/> if no connection is associated.</summary>
    object? Connection { get; }

    /// <summary>Returns the underlying transaction cast to <typeparamref name="T"/>, or <see langword="null"/> if the cast fails.</summary>
    /// <typeparam name="T">The target transaction type to cast to.</typeparam>
    /// <returns>The transaction cast to <typeparamref name="T"/>, or <see langword="null"/> if the cast is not valid.</returns>
    T? GetContext<T>() where T : class => Transaction as T;
}
