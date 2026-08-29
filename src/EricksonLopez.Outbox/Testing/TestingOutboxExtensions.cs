// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Linq;

namespace EricksonLopez.Outbox.Testing;

/// <summary>
/// Extension methods that add fluent, expressive assertion helpers for outbox-based tests.
///
/// <para>
/// These extensions bridge <see cref="IOutboxTester"/> and <see cref="InMemoryOutboxStore"/>
/// to provide a high-signal, low-ceremony testing API without requiring a mocking framework.
/// </para>
///
/// <example>
/// <code>
/// // Setup
/// var store = new InMemoryOutboxStore();
/// services.AddSingleton&lt;IOutbox&gt;(store);
/// var tester = new OutboxTesterImpl(store);
///
/// // Act
/// await sut.DoSomethingAsync();
///
/// // Assert
/// tester.ShouldHavePublished&lt;OrderCreatedEvent&gt;()
///       .WithCondition(e => e.OrderId == expectedId)
///       .Once();
///
/// // Or using the shorthand extensions:
/// store.ShouldHavePublished&lt;OrderCreatedEvent&gt;(e => e.OrderId == expectedId);
/// store.ShouldHavePublishedOnce&lt;OrderCreatedEvent&gt;();
/// store.ShouldNotHavePublished&lt;OrderCancelledEvent&gt;();
/// </code>
/// </example>
/// </summary>
public static class TestingOutboxExtensions
{
    // -------------------------------------------------------------------------
    // InMemoryOutboxStore direct extensions — zero IOutboxTester allocation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Asserts that at least one message of type <typeparamref name="TMessage"/> was stored.
    /// </summary>
    /// <typeparam name="TMessage">The type of the message to assert on.</typeparam>
    /// <param name="store">The in-memory outbox store.</param>
    /// <returns>All stored messages of that type for further inspection.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no messages of the type were found.</exception>
    public static IReadOnlyList<TMessage> ShouldHavePublished<TMessage>(
        this InMemoryOutboxStore store)
        where TMessage : notnull
    {
        var messages = store.GetPublishedMessages<TMessage>();
        if (messages.Count == 0)
            throw new InvalidOperationException(
                $"Expected at least one message of type {typeof(TMessage).Name} to be published, but none were found.");
        return messages;
    }

    /// <summary>
    /// Asserts that at least one message of type <typeparamref name="TMessage"/> was stored
    /// and that at least one message matches the given predicate.
    /// </summary>
    /// <typeparam name="TMessage">The type of the message to assert on.</typeparam>
    /// <param name="store">The in-memory outbox store.</param>
    /// <param name="predicate">A predicate to filter matching messages.</param>
    /// <returns>All stored messages of that type that match the predicate.</returns>
    public static IReadOnlyList<TMessage> ShouldHavePublished<TMessage>(
        this InMemoryOutboxStore store,
        Func<TMessage, bool> predicate)
        where TMessage : notnull
    {
        var all = store.GetPublishedMessages<TMessage>();
        var matching = all.Where(predicate).ToList();
        if (matching.Count == 0)
        {
            throw new InvalidOperationException(
                $"Expected at least one message of type {typeof(TMessage).Name} matching the predicate, " +
                $"but none were found. ({all.Count} messages of that type were stored in total.)");
        }
        return matching;
    }

    /// <summary>
    /// Asserts that exactly one message of type <typeparamref name="TMessage"/> was stored.
    /// </summary>
    /// <typeparam name="TMessage">The type of the message to assert on.</typeparam>
    /// <param name="store">The in-memory outbox store.</param>
    /// <returns>The single published message.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the count is not exactly 1.</exception>
    public static TMessage ShouldHavePublishedOnce<TMessage>(
        this InMemoryOutboxStore store)
        where TMessage : notnull
    {
        var messages = store.GetPublishedMessages<TMessage>();
        if (messages.Count != 1)
            throw new InvalidOperationException(
                $"Expected exactly one message of type {typeof(TMessage).Name} to be published, " +
                $"but found {messages.Count}.");
        return messages[0];
    }

    /// <summary>
    /// Asserts that exactly one message of type <typeparamref name="TMessage"/> matching the
    /// given predicate was stored.
    /// </summary>
    /// <typeparam name="TMessage">The type of the message to assert on.</typeparam>
    /// <param name="store">The in-memory outbox store.</param>
    /// <param name="predicate">A predicate to filter matching messages.</param>
    /// <returns>The single matching message.</returns>
    public static TMessage ShouldHavePublishedOnce<TMessage>(
        this InMemoryOutboxStore store,
        Func<TMessage, bool> predicate)
        where TMessage : notnull
    {
        var all = store.GetPublishedMessages<TMessage>();
        var matching = all.Where(predicate).ToList();
        if (matching.Count != 1)
            throw new InvalidOperationException(
                $"Expected exactly one message of type {typeof(TMessage).Name} matching the predicate, " +
                $"but found {matching.Count}. ({all.Count} messages of that type were stored in total.)");
        return matching[0];
    }

    /// <summary>
    /// Asserts that exactly <paramref name="count"/> messages of type
    /// <typeparamref name="TMessage"/> were stored.
    /// </summary>
    /// <typeparam name="TMessage">The type of the message to assert on.</typeparam>
    /// <param name="store">The in-memory outbox store.</param>
    /// <param name="count">The expected number of published messages.</param>
    /// <returns>All stored messages of that type.</returns>
    public static IReadOnlyList<TMessage> ShouldHavePublishedTimes<TMessage>(
        this InMemoryOutboxStore store,
        int count)
        where TMessage : notnull
    {
        var messages = store.GetPublishedMessages<TMessage>();
        if (messages.Count != count)
            throw new InvalidOperationException(
                $"Expected exactly {count} message(s) of type {typeof(TMessage).Name} to be published, " +
                $"but found {messages.Count}.");
        return messages;
    }

    /// <summary>
    /// Asserts that no messages of type <typeparamref name="TMessage"/> were stored.
    /// </summary>
    /// <typeparam name="TMessage">The type of the message to assert on.</typeparam>
    /// <param name="store">The in-memory outbox store.</param>
    /// <exception cref="InvalidOperationException">Thrown when any message of the type was found.</exception>
    public static void ShouldNotHavePublished<TMessage>(
        this InMemoryOutboxStore store)
        where TMessage : notnull
    {
        var messages = store.GetPublishedMessages<TMessage>();
        if (messages.Count > 0)
            throw new InvalidOperationException(
                $"Expected {typeof(TMessage).Name} to never be published, but {messages.Count} were found.");
    }

    /// <summary>
    /// Asserts that no messages of type <typeparamref name="TMessage"/> matching the given
    /// predicate were stored.
    /// </summary>
    /// <typeparam name="TMessage">The type of the message to assert on.</typeparam>
    /// <param name="store">The in-memory outbox store.</param>
    /// <param name="predicate">A predicate to filter matching messages.</param>
    public static void ShouldNotHavePublished<TMessage>(
        this InMemoryOutboxStore store,
        Func<TMessage, bool> predicate)
        where TMessage : notnull
    {
        var all = store.GetPublishedMessages<TMessage>();
        var matching = all.Where(predicate).ToList();
        if (matching.Count > 0)
            throw new InvalidOperationException(
                $"Expected no messages of type {typeof(TMessage).Name} matching the predicate, " +
                $"but found {matching.Count}.");
    }

    /// <summary>
    /// Returns the total number of stored messages across all types.
    /// Useful for asserting that an operation produced a specific number of side effects.
    /// </summary>
    /// <param name="store">The in-memory outbox store.</param>
    /// <returns>The total count of published messages.</returns>
    public static int TotalPublishedCount(this InMemoryOutboxStore store)
        => store.GetPublishedMessages<object>().Count;

    // -------------------------------------------------------------------------
    // IOutboxTester extensions — extended fluent API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Asserts that no messages of type <typeparamref name="TMessage"/> were published.
    /// Syntactic sugar over <see cref="IOutboxAssertion{TMessage}.Never"/>.
    /// </summary>
    /// <typeparam name="TMessage">The type of the message to assert on.</typeparam>
    /// <param name="tester">The outbox tester instance.</param>
    /// <example>
    /// <code>
    /// tester.ShouldNotHavePublished&lt;OrderCancelledEvent&gt;();
    /// </code>
    /// </example>
    public static void ShouldNotHavePublished<TMessage>(this IOutboxTester tester)
        where TMessage : notnull
        => tester.ShouldHavePublished<TMessage>().Never();

    /// <summary>
    /// Asserts that exactly one message of type <typeparamref name="TMessage"/> was published.
    /// Syntactic sugar over <see cref="IOutboxAssertion{TMessage}.Once"/>.
    /// </summary>
    /// <typeparam name="TMessage">The type of the message to assert on.</typeparam>
    /// <param name="tester">The outbox tester instance.</param>
    public static void ShouldHavePublishedOnce<TMessage>(this IOutboxTester tester)
        where TMessage : notnull
        => tester.ShouldHavePublished<TMessage>().Once();

    /// <summary>
    /// Asserts that exactly one message of type <typeparamref name="TMessage"/> matching
    /// the given predicate was published.
    /// </summary>
    /// <typeparam name="TMessage">The type of the message to assert on.</typeparam>
    /// <param name="tester">The outbox tester instance.</param>
    /// <param name="predicate">A predicate to filter matching messages.</param>
    public static void ShouldHavePublishedOnce<TMessage>(
        this IOutboxTester tester,
        Func<TMessage, bool> predicate)
        where TMessage : notnull
        => tester.ShouldHavePublished<TMessage>().WithCondition(predicate).Once();

    /// <summary>
    /// Asserts that at least one message of type <typeparamref name="TMessage"/> was published
    /// matching the given predicate.
    /// </summary>
    /// <typeparam name="TMessage">The type of the message to assert on.</typeparam>
    /// <param name="tester">The outbox tester instance.</param>
    /// <param name="predicate">A predicate to filter matching messages.</param>
    public static void ShouldHavePublished<TMessage>(
        this IOutboxTester tester,
        Func<TMessage, bool> predicate)
        where TMessage : notnull
        => tester.ShouldHavePublished<TMessage>().WithCondition(predicate).AtLeastOnce();

    /// <summary>
    /// Asserts that exactly <paramref name="times"/> messages of type <typeparamref name="TMessage"/>
    /// were published.
    /// </summary>
    /// <typeparam name="TMessage">The type of the message to assert on.</typeparam>
    /// <param name="tester">The outbox tester instance.</param>
    /// <param name="times">The expected occurrence count.</param>
    public static void ShouldHavePublishedTimes<TMessage>(
        this IOutboxTester tester,
        int times)
        where TMessage : notnull
        => tester.ShouldHavePublished<TMessage>().Times(times);
}

