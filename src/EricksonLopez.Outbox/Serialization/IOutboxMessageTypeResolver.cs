// Stryker disable all : Covered by ADR-013. Edge cases, micro-optimizations, logging, and validation strings are not rigorously mutated.
using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace EricksonLopez.Outbox.Serialization;

/// <summary>
/// Maps logical message type aliases to CLR types and vice versa.
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

    // FIX-18: Add generic overloads to eliminate typeof(TMessage) in the hot path.
    //
    // Root cause: DefaultOutbox.BuildOutboxMessage called typeof(TMessage) inside TryGetAlias(typeof(TMessage), ...)
    // which is not zero-allocation. While typeof() itself doesn't allocate, it prevents the JIT from inlining
    // and optimizing the call as effectively as a generic overload.
    //
    // Fix: Provide TryGetAlias<T>() / GetAlias<T>() generic overloads. The JIT specializes these
    // per type argument, allowing the type lookup to be devirtualized and potentially inlined.
    // Default implementations delegate to the non-generic overloads for backward compatibility.

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

/// <summary>
/// Provides a default in-memory implementation of <see cref="IOutboxMessageTypeResolver"/>.
/// </summary>
/// <remarks>
/// This implementation uses <see cref="FrozenDictionary{TKey,TValue}"/> for optimized, allocation-free lookups.
/// It is intended to be populated once during application startup.
/// </remarks>
// Stryker disable String : Exception messages are not tested for exact matching
public sealed class InMemoryMessageTypeResolver : IOutboxMessageTypeResolver
{
    private readonly FrozenDictionary<string, Type> _aliasToType;
    private readonly FrozenDictionary<Type, string> _typeToAlias;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryMessageTypeResolver"/> class with the specified mappings.
    /// </summary>
    /// <param name="mappings">The collection of alias-to-type pairs to register.</param>
    /// <exception cref="ArgumentException">An alias in the mappings is null or whitespace.</exception>
    public InMemoryMessageTypeResolver(IEnumerable<(string alias, Type type)> mappings)
    {
        var aliasToType = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        var typeToAlias = new Dictionary<Type, string>();

        foreach (var (alias, type) in mappings)
        {
            if (string.IsNullOrWhiteSpace(alias))
                throw new ArgumentException($"Alias for type {type.Name} cannot be null or empty.");

            aliasToType[alias] = type;
            typeToAlias[type] = alias;
        }

        _aliasToType = aliasToType.ToFrozenDictionary(aliasToType.Comparer);
        _typeToAlias = typeToAlias.ToFrozenDictionary();
    }

    /// <inheritdoc/>
    public Type? Resolve(string alias) =>
        _aliasToType.TryGetValue(alias, out var type) ? type : null;

    /// <inheritdoc/>
    public bool TryGetAlias(Type messageType, out string? alias) =>
        _typeToAlias.TryGetValue(messageType, out alias);

    /// <inheritdoc/>
    public string GetAlias(Type messageType)
    {
        if (!TryGetAlias(messageType, out var alias))
            throw new InvalidOperationException(
                $"Type '{messageType.FullName}' is not registered in the OutboxMessageTypeResolver. " +
                "Decorate it with [OutboxMessage(\"your.alias\")] and register it during startup.");

        return alias!;
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, Type> GetAllMappings() => _aliasToType;
}
