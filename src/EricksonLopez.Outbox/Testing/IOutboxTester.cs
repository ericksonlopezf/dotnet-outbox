// Copyright © Erickson Lopez. MIT License.
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

