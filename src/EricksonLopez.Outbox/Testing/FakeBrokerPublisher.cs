using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using EricksonLopez.Outbox;

namespace EricksonLopez.Outbox.Testing;

/// <summary>
/// A fake <see cref="IBrokerPublisher"/> implementation designed for use in unit and integration tests.
/// </summary>
/// <remarks>
/// <para>
/// This fake captures all published messages in memory, allowing you to assert that specific messages
/// were dispatched during a test scenario without requiring a real message broker.
/// </para>
/// <para>
/// <b>Usage example:</b>
/// <code>
///   var fakeBroker = new FakeBrokerPublisher();
///   // ... configure DI with fakeBroker
///   fakeBroker.ShouldHavePublished("order.created.v1").Once();
/// </code>
/// </para>
/// </remarks>
public sealed class FakeBrokerPublisher : IBrokerPublisher
{
    private readonly ConcurrentQueue<PublishedRawMessage> _rawMessages = new();
    private bool _shouldFail;
    private Exception? _failureException;

    /// <summary>
    /// Gets all raw messages captured during the test execution.
    /// </summary>
    public IReadOnlyList<PublishedRawMessage> CapturedMessages => _rawMessages.ToArray();

    /// <summary>
    /// Configures the fake broker to simulate a failure result for subsequent publish calls.
    /// </summary>
    /// <param name="ex">The optional exception representing the failure. If not provided, a default <see cref="InvalidOperationException"/> is used.</param>
    /// <returns>The current <see cref="FakeBrokerPublisher"/> instance for chaining.</returns>
    public FakeBrokerPublisher WithFailure(Exception? ex = null)
    {
        _shouldFail = true;
        _failureException = ex ?? new InvalidOperationException("Simulated broker failure.");
        return this;
    }

    /// <summary>
    /// Configures the fake broker to return successful results, resetting any previous failure simulations.
    /// </summary>
    /// <returns>The current <see cref="FakeBrokerPublisher"/> instance for chaining.</returns>
    public FakeBrokerPublisher WithSuccess()
    {
        _shouldFail = false;
        _failureException = null;
        return this;
    }

    /// <inheritdoc/>
    public ValueTask<DispatchResult> PublishAsync<T>(
        MessageEnvelope<T> message,
        DispatchContext context) where T : notnull
    {
        if (_shouldFail)
            return new ValueTask<DispatchResult>(DispatchResult.FailAndRetry(_failureException!));

        // We can't capture the strongly-typed payload without serialization;
        // for testing, the strongly-typed path stores metadata only.
        _rawMessages.Enqueue(new PublishedRawMessage(
            MessageType: message.Metadata.MessageType ?? typeof(T).Name,
            Payload: ReadOnlyMemory<byte>.Empty,
            Metadata: message.Metadata));

        return new ValueTask<DispatchResult>(DispatchResult.Ok());
    }

    /// <inheritdoc/>
    public async ValueTask<IReadOnlyList<DispatchResult>> PublishBatchAsync<T>(
        IReadOnlyList<MessageEnvelope<T>> messages,
        DispatchContext context) where T : notnull
    {
        var results = new List<DispatchResult>(messages.Count);
        foreach (var msg in messages)
            results.Add(await PublishAsync(msg, context));
        return results;
    }

    /// <inheritdoc/>
    public ValueTask<DispatchResult> PublishRawAsync(
        OutboxMessage message,
        MessageMetadata metadata,
        DispatchContext context)
    {
        if (_shouldFail)
            return new ValueTask<DispatchResult>(DispatchResult.FailAndRetry(_failureException!));

        _rawMessages.Enqueue(new PublishedRawMessage(
            MessageType: message.MessageType,
            Payload: message.Payload,
            Metadata: metadata));

        return new ValueTask<DispatchResult>(DispatchResult.Ok());
    }

    /// <summary>
    /// Provides a fluent assertion to verify that a message matching the specified type alias was published.
    /// </summary>
    /// <param name="messageTypeAlias">The string alias representing the message type.</param>
    /// <returns>An assertion interface to chain further constraints.</returns>
    public IFakeBrokerAssertion ShouldHavePublished(string messageTypeAlias) =>
        new FakeBrokerAssertion(_rawMessages, messageTypeAlias);

    /// <summary>
    /// Clears all captured messages, resetting the state of the fake broker.
    /// </summary>
    public void Reset() => _rawMessages.Clear();
}

/// <summary>
/// Represents a raw message that was published and captured by the fake broker.
/// </summary>
/// <param name="MessageType">The string alias of the message type.</param>
/// <param name="Payload">The raw serialized payload of the message.</param>
/// <param name="Metadata">The metadata associated with the message.</param>
public sealed record PublishedRawMessage(
    string MessageType,
    ReadOnlyMemory<byte> Payload,
    MessageMetadata Metadata);

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

internal sealed class FakeBrokerAssertion : IFakeBrokerAssertion
{
    private readonly IEnumerable<PublishedRawMessage> _captured;
    private readonly string _typeAlias;
    private string? _correlationId;

    public FakeBrokerAssertion(IEnumerable<PublishedRawMessage> captured, string typeAlias)
    {
        _captured = captured;
        _typeAlias = typeAlias;
    }

    public IFakeBrokerAssertion WithCorrelationId(string correlationId)
    {
        _correlationId = correlationId;
        return this;
    }

    public void Once() => Times(1);

    public void Times(int count)
    {
        var actual = CountMatching();
        if (actual != count)
            throw new InvalidOperationException(
                $"Expected '{_typeAlias}' to be published {count} time(s), but was {actual}.");
    }

    public void AtLeastOnce()
    {
        if (CountMatching() == 0)
            throw new InvalidOperationException(
                $"Expected '{_typeAlias}' to be published at least once, but it was never published.");
    }

    public void Never()
    {
        var count = CountMatching();
        if (count > 0)
            throw new InvalidOperationException(
                $"Expected '{_typeAlias}' to never be published, but it was published {count} time(s).");
    }

    private int CountMatching()
    {
        int count = 0;
        foreach (var msg in _captured)
        {
            if (!string.Equals(msg.MessageType, _typeAlias, StringComparison.OrdinalIgnoreCase))
                continue;

            if (_correlationId is not null
                && !string.Equals(msg.Metadata.CorrelationId, _correlationId, StringComparison.Ordinal))
                continue;

            count++;
        }
        return count;
    }
}
