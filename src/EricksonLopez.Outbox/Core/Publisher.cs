// Stryker disable all : Covered by ADR-013. Edge cases, micro-optimizations, logging, and validation strings are not rigorously mutated.
using System;

namespace EricksonLopez.Outbox;

/// <summary>
/// Represents a logical publisher identity within the outbox ecosystem.
/// </summary>
/// <remarks>
/// This record is used in multi-publisher scenarios to identify which specific publisher instance
/// or node is responsible for dispatching a given batch of messages.
/// </remarks>
/// <param name="Id">The unique identifier of the publisher instance.</param>
/// <param name="Name">The human-readable name or role of the publisher.</param>
/// <param name="RegisteredAt">The exact date and time the publisher was registered or started.</param>
// Stryker disable String : Identifiers, exception messages, and constants are not tested for exact matching
public readonly record struct Publisher(
    string Id,
    string Name,
    DateTimeOffset RegisteredAt)
{
    /// <summary>
    /// Creates a new publisher with a unique identifier and the current timestamp.
    /// </summary>
    /// <param name="name">The human-readable name of the publisher.</param>
    /// <returns>A new <see cref="Publisher"/> instance with an auto-generated unique ID.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is <see langword="null"/> or empty.</exception>
    public static Publisher Create(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Publisher name cannot be null or empty.", nameof(name));

        return new Publisher(
            Id: Guid.NewGuid().ToString("N"),
            Name: name,
            RegisteredAt: DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Represents a null-object publisher for scenarios where publisher identity is not required.
    /// </summary>
    public static readonly Publisher None = new(
        Id: "00000000000000000000000000000000",
        Name: "none",
        RegisteredAt: DateTimeOffset.MinValue);
}
