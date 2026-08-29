// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace EricksonLopez.Outbox.Serialization;

/// <summary>
/// Provides a default in-memory implementation of <see cref="IOutboxMessageTypeResolver"/>.
/// </summary>
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
