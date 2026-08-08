using System;

namespace EricksonLopez.Outbox.Testing;

/// <summary>
/// Defines a fluent assertion interface to verify that domain events were correctly published to the Outbox.
/// </summary>
/// <remarks>
/// Allows for expressive, robust tests that verify outbox side effects without
/// relying on brittle mocking framework verifications (e.g., <c>Mock.Verify()</c>).
/// </remarks>
public interface IOutboxTester
{
    /// <summary>
    /// Asserts that an event of the specified type was published to the outbox.
    /// </summary>
    /// <typeparam name="TMessage">The type of the message to assert on.</typeparam>
    /// <returns>A fluent assertion builder to chain further conditions (e.g., payload assertions or expected occurrences).</returns>
    IOutboxAssertion<TMessage> ShouldHavePublished<TMessage>() where TMessage : notnull;
}

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
