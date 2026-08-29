// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Outbox.Testing;

/// <summary>
/// Defines an interface for chaining fluid assertions on a published outbox message of a specific type.
/// </summary>
/// <typeparam name="TMessage">The type of the message being asserted.</typeparam>
public interface IOutboxAssertion<out TMessage>
{
    /// <summary>
    /// Applies a predicate condition to constrain the assertion to matching message payloads.
    /// </summary>
    /// <param name="predicate">A function to test each message payload for a condition.</param>
    /// <returns>The current assertion interface for chaining.</returns>
    IOutboxAssertion<TMessage> WithCondition(Func<TMessage, bool> predicate);

    /// <summary>
    /// Asserts that the matching message was published exactly once.
    /// </summary>
    void Once();

    /// <summary>
    /// Asserts that the matching message was published exactly the specified number of times.
    /// </summary>
    /// <param name="count">The exact number of times the message should have been published.</param>
    void Times(int count);

    /// <summary>
    /// Asserts that the matching message was published at least once.
    /// </summary>
    void AtLeastOnce();

    /// <summary>
    /// Asserts that the matching message was never published.
    /// </summary>
    void Never();
}
