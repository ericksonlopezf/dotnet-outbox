// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Outbox.Contracts;

/// <summary>
/// Marks a class or record as a valid message or event for the outbox processing pipeline.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
public sealed class OutboxMessageAttribute : Attribute
{
    /// <summary>
    /// Gets the unique alias (Type Mapping) that will be stored in the database.
    /// </summary>
    public string Alias { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="OutboxMessageAttribute"/> class.
    /// </summary>
    /// <param name="alias">The stable string alias identifying the message type.</param>
    /// <exception cref="ArgumentException"><paramref name="alias"/> is <see langword="null"/> or white space.</exception>
    public OutboxMessageAttribute(string alias)
    {
        if (string.IsNullOrWhiteSpace(alias))
            throw new ArgumentException("The alias cannot be null or empty.", nameof(alias));

        Alias = alias;
    }
}
