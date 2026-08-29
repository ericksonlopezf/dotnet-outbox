// Copyright © Erickson Lopez. MIT License.

namespace EricksonLopez.Outbox;

/// <summary>
/// Represents a single key-value pair of metadata associated with a message.
/// </summary>
/// <param name="Key">The key identifying the metadata entry.</param>
/// <param name="Value">The value of the metadata entry.</param>
public readonly record struct MetadataEntry(string Key, string Value);
