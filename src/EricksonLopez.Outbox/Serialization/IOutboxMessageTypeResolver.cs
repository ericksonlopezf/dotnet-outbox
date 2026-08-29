// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Threading;

namespace EricksonLopez.Outbox.Serialization;

/// <summary>
/// Defines a contract for mapping logical message type aliases to CLR types and vice versa.
/// </summary>
/// <remarks>
/// This indirection solves a critical versioning problem: if a class is renamed or moved to a different
/// namespace/assembly, the alias stored in the database remains stable. The application only needs to
/// update the mapping registry, not perform a data migration.
/// Designed to be registered once at startup and used read-only afterwards (thread-safe by construction).
/// </remarks>
public interface IOutboxMessageTypeResolver
{
    /// <summary>
    /// Resolves the alias string to its corresponding CLR type.
    /// </summary>
    /// <param name="alias">The stable string alias identifying the message type.</param>
    /// <returns>The resolved <see cref="Type"/> if found; otherwise, <see langword="null"/>.</returns>
    Type? Resolve(string alias);

    /// <summary>
    /// Resolves a CLR type to its registered alias string without throwing an exception on missing registration.
    /// </summary>
    /// <param name="messageType">The CLR type of the message to resolve.</param>
    /// <param name="alias">When it returns, contains the registered alias string if found; otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the alias was found; otherwise, <see langword="false"/>.</returns>
    bool TryGetAlias(Type messageType, out string? alias);

    /// <summary>
    /// Resolves a CLR type to its registered alias string.
    /// </summary>
    /// <param name="messageType">The CLR type of the message to resolve.</param>
    /// <returns>The registered alias string.</returns>
    /// <exception cref="InvalidOperationException">The specified type has not been registered.</exception>
    string GetAlias(Type messageType);

    /// <summary>
    /// Retrieves all registered alias-to-type mappings.
    /// </summary>
    /// <returns>A read-only dictionary containing all registered mappings.</returns>
    IReadOnlyDictionary<string, Type> GetAllMappings();

    /// <summary>
    /// Resolves the alias for the specified generic message type without requiring a runtime type lookup.
    /// </summary>
    /// <typeparam name="TMessage">The generic type of the message to resolve.</typeparam>
    /// <param name="alias">When it returns, contains the registered alias string if found; otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> if the alias was found; otherwise, <see langword="false"/>.</returns>
    bool TryGetAlias<TMessage>(out string? alias) where TMessage : notnull
    {
        return TryGetAlias(typeof(TMessage), out alias);
    }

    /// <summary>
    /// Resolves the alias for the specified generic message type.
    /// </summary>
    /// <typeparam name="TMessage">The generic type of the message to resolve.</typeparam>
    /// <returns>The registered alias string.</returns>
    /// <exception cref="InvalidOperationException">The specified type has not been registered.</exception>
    string GetAlias<TMessage>() where TMessage : notnull
    {
        return GetAlias(typeof(TMessage));
    }
}


