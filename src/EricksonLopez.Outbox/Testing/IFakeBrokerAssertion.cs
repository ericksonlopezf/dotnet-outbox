// Copyright © Erickson Lopez. MIT License.

namespace EricksonLopez.Outbox.Testing;

/// <summary>
/// Defines a fluent assertion interface for verifying messages published to the fake broker.
/// </summary>
public interface IFakeBrokerAssertion
{
    /// <summary>
    /// Constrains the assertion to match only messages with the specified correlation ID.
    /// </summary>
    /// <param name="correlationId">The correlation ID to filter by.</param>
    /// <returns>The current assertion interface for chaining.</returns>
    IFakeBrokerAssertion WithCorrelationId(string correlationId);

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
