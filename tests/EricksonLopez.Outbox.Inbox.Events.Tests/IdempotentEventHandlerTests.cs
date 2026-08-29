// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Events.Contracts;
using EricksonLopez.Events.Envelopes;
using EricksonLopez.Events.Identifiers;
using EricksonLopez.Events.Metadata;
using EricksonLopez.Inbox;
using EricksonLopez.Outbox.Inbox.Events;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.Core;
using Xunit;
using EventId = EricksonLopez.Events.Identifiers.EventId;

namespace EricksonLopez.Outbox.Inbox.Events.Tests;

[Trait("Category", "Unit")]
public sealed class IdempotentEventHandlerTests
{
    public sealed record SampleOrderPlaced(EventId Id, string OrderNumber, DateTimeOffset OccurredAt) : IIntegrationEvent;

    public sealed record SampleEnvelopeEvent(EventId Id, EventType Type, EventVersion Version, DateTimeOffset OccurredAt, EventMetadata Metadata) : IEvent, IEventEnvelope
    {
        public object GetPayload() => this;
    }

    public sealed class SampleOrderPlacedHandler : IEventHandler<SampleOrderPlaced>
    {
        public bool Handled { get; private set; }

        public ValueTask HandleAsync(SampleOrderPlaced eventInstance, CancellationToken cancellationToken = default)
        {
            Handled = true;
            return ValueTask.CompletedTask;
        }
    }

    public sealed class SampleEnvelopeEventHandler : IEventHandler<SampleEnvelopeEvent>
    {
        public bool Handled { get; private set; }

        public ValueTask HandleAsync(SampleEnvelopeEvent eventInstance, CancellationToken cancellationToken = default)
        {
            Handled = true;
            return ValueTask.CompletedTask;
        }
    }

    private readonly IEventHandler<SampleOrderPlaced> _innerHandler = Substitute.For<IEventHandler<SampleOrderPlaced>>();
    private readonly IInboxConsumerFilter _inboxFilter = Substitute.For<IInboxConsumerFilter>();
    private readonly ILogger<IdempotentEventHandler<SampleOrderPlaced>> _logger = Substitute.For<ILogger<IdempotentEventHandler<SampleOrderPlaced>>>();

    [Fact]
    public void Constructor_NullGuards_ThrowArgumentNullException()
    {
        Action act1 = () => _ = new IdempotentEventHandler<SampleOrderPlaced>(null!, _inboxFilter);
        act1.Should().Throw<ArgumentNullException>().WithParameterName("innerHandler");

        Action act2 = () => _ = new IdempotentEventHandler<SampleOrderPlaced>(_innerHandler, null!);
        act2.Should().Throw<ArgumentNullException>().WithParameterName("inboxFilter");
    }

    [Fact]
    public async Task Constructor_DefaultConsumerName_SetsFallbackToInnerHandlerTypeName()
    {
        string? capturedConsumerName = null;
        _inboxFilter.ExecuteIdempotentlyAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<Func<CancellationToken, ValueTask>>(),
            Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedConsumerName = callInfo.ArgAt<string>(1);
                return ValueTask.FromResult(true);
            });

        var realHandler = new SampleOrderPlacedHandler();
        var sut = new IdempotentEventHandler<SampleOrderPlaced>(realHandler, _inboxFilter);
        var evt = new SampleOrderPlaced(EventId.New(), "ORD-123", DateTimeOffset.UtcNow);

        await sut.HandleAsync(evt);

        capturedConsumerName.Should().Be(typeof(SampleOrderPlacedHandler).FullName);
    }

    [Fact]
    public void Constructor_WithAllParameters_InitializesCorrectly()
    {
        var sut = new IdempotentEventHandler<SampleOrderPlaced>(_innerHandler, _inboxFilter, "CustomConsumer", _logger);
        sut.Should().NotBeNull();
    }

    [Fact]
    public async Task HandleAsync_NullEventInstance_ThrowsArgumentNullException()
    {
        var sut = new IdempotentEventHandler<SampleOrderPlaced>(_innerHandler, _inboxFilter);
        Func<Task> act = async () => await sut.HandleAsync(null!);
        await act.Should().ThrowExactlyAsync<ArgumentNullException>().WithParameterName("eventInstance");
    }

    [Fact]
    public async Task HandleAsync_WhenEventImplementsIEventEnvelope_DerivesMessageIdFromEnvelopeId()
    {
        string? capturedMessageId = null;
        var envelopeHandler = Substitute.For<IEventHandler<SampleEnvelopeEvent>>();
        var envelopeFilter = Substitute.For<IInboxConsumerFilter>();

        envelopeFilter.ExecuteIdempotentlyAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<Func<CancellationToken, ValueTask>>(),
            Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedMessageId = callInfo.ArgAt<string>(0);
                return ValueTask.FromResult(true);
            });

        var sut = new IdempotentEventHandler<SampleEnvelopeEvent>(envelopeHandler, envelopeFilter, "EnvelopeConsumer");
        var eventId = EventId.New();
#pragma warning disable CS8625
        var envelopeEvt = new SampleEnvelopeEvent(
            eventId,
            default,
            default,
            DateTimeOffset.UtcNow,
            default);
#pragma warning restore CS8625

        await sut.HandleAsync(envelopeEvt);

        capturedMessageId.Should().Be(eventId.ToString());
    }

    [Fact]
    public async Task HandleAsync_WhenEventDoesNotImplementIEventEnvelope_DerivesMessageIdFromTypeAndHashCode()
    {
        string? capturedMessageId = null;
        _inboxFilter.ExecuteIdempotentlyAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<Func<CancellationToken, ValueTask>>(),
            Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedMessageId = callInfo.ArgAt<string>(0);
                return ValueTask.FromResult(true);
            });

        var sut = new IdempotentEventHandler<SampleOrderPlaced>(_innerHandler, _inboxFilter, "OrderConsumer");
        var evt = new SampleOrderPlaced(EventId.New(), "ORD-999", DateTimeOffset.UtcNow);

        await sut.HandleAsync(evt);

        var expectedMessageId = $"{typeof(SampleOrderPlaced).Name}:{evt.GetHashCode()}";
        capturedMessageId.Should().Be(expectedMessageId);
    }

    [Fact]
    public async Task HandleAsync_WhenHandledIsTrue_ExecutesInnerHandler_AndDoesNotLogSkipped()
    {
        using var cts = new CancellationTokenSource();
        _logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        Func<CallInfo, ValueTask<bool>> callback = async callInfo =>
        {
            var handler = callInfo.Arg<Func<CancellationToken, ValueTask>>();
            var ct = callInfo.Arg<CancellationToken>();
            await handler(ct);
            return true;
        };

        _inboxFilter.ExecuteIdempotentlyAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<Func<CancellationToken, ValueTask>>(),
            Arg.Any<CancellationToken>())
            .Returns(callback);

        var sut = new IdempotentEventHandler<SampleOrderPlaced>(_innerHandler, _inboxFilter, "OrderConsumer", _logger);
        var evt = new SampleOrderPlaced(EventId.New(), "ORD-999", DateTimeOffset.UtcNow);

        await sut.HandleAsync(evt, cts.Token);

        await _inboxFilter.Received(1).ExecuteIdempotentlyAsync(
            $"{typeof(SampleOrderPlaced).Name}:{evt.GetHashCode()}",
            "OrderConsumer",
            Arg.Any<Func<CancellationToken, ValueTask>>(),
            cts.Token);

        await _innerHandler.Received(1).HandleAsync(evt, cts.Token);

        _logger.DidNotReceive().Log(
            LogLevel.Debug,
            Arg.Any<Microsoft.Extensions.Logging.EventId>(),
            Arg.Any<object>(),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task HandleAsync_WhenHandledIsFalse_DoesNotExecuteInnerHandler_AndLogsSkipped()
    {
        using var cts = new CancellationTokenSource();
        _logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        _inboxFilter.ExecuteIdempotentlyAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<Func<CancellationToken, ValueTask>>(),
            Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(false));

        var sut = new IdempotentEventHandler<SampleOrderPlaced>(_innerHandler, _inboxFilter, "OrderConsumer", _logger);
        var evt = new SampleOrderPlaced(EventId.New(), "ORD-999", DateTimeOffset.UtcNow);
        var expectedMessageId = $"{typeof(SampleOrderPlaced).Name}:{evt.GetHashCode()}";

        await sut.HandleAsync(evt, cts.Token);

        await _innerHandler.DidNotReceiveWithAnyArgs().HandleAsync(default!, default);

        var logCalls = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Where(_logger.ReceivedCalls(), c => c.GetMethodInfo().Name == "Log"));
        logCalls.Should().ContainSingle();
        var call = logCalls[0];
        call.GetArguments()[0].Should().Be(LogLevel.Debug);
        var logMessage = call.GetArguments()[2]?.ToString();
        logMessage.Should().NotBeNull();
        logMessage.Should().Contain("Event 'SampleOrderPlaced'");
        logMessage.Should().Contain($"with ID '{expectedMessageId}'");
        logMessage.Should().Contain("was skipped as a duplicate by consumer 'OrderConsumer'");
    }

    [Fact]
    public async Task HandleAsync_WhenEnvelopeEventIsDuplicate_LogsEnvelopeIdInSkippedMessage()
    {
        var envelopeHandler = Substitute.For<IEventHandler<SampleEnvelopeEvent>>();
        var envelopeFilter = Substitute.For<IInboxConsumerFilter>();
        var envelopeLogger = Substitute.For<ILogger<IdempotentEventHandler<SampleEnvelopeEvent>>>();
        envelopeLogger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);

        envelopeFilter.ExecuteIdempotentlyAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<Func<CancellationToken, ValueTask>>(),
            Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(false));

        var sut = new IdempotentEventHandler<SampleEnvelopeEvent>(envelopeHandler, envelopeFilter, "EnvelopeConsumer", envelopeLogger);
        var eventId = EventId.New();
#pragma warning disable CS8625
        var envelopeEvt = new SampleEnvelopeEvent(
            eventId,
            default,
            default,
            DateTimeOffset.UtcNow,
            default);
#pragma warning restore CS8625

        await sut.HandleAsync(envelopeEvt);

        await envelopeHandler.DidNotReceiveWithAnyArgs().HandleAsync(default!, default);

        var logCalls = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Where(envelopeLogger.ReceivedCalls(), c => c.GetMethodInfo().Name == "Log"));
        logCalls.Should().ContainSingle();
        var call = logCalls[0];
        call.GetArguments()[0].Should().Be(LogLevel.Debug);
        var logMessage = call.GetArguments()[2]?.ToString();
        logMessage.Should().NotBeNull();
        logMessage.Should().Contain("Event 'SampleEnvelopeEvent'");
        logMessage.Should().Contain($"with ID '{eventId}'");
        logMessage.Should().Contain("was skipped as a duplicate by consumer 'EnvelopeConsumer'");
    }

    [Fact]
    public async Task HandleAsync_WhenDuplicateOccursWithoutLogger_CompletesGracefully()
    {
        _inboxFilter.ExecuteIdempotentlyAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<Func<CancellationToken, ValueTask>>(),
            Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(false));

        var sut = new IdempotentEventHandler<SampleOrderPlaced>(_innerHandler, _inboxFilter, "OrderConsumer", logger: null);
        var evt = new SampleOrderPlaced(EventId.New(), "ORD-999", DateTimeOffset.UtcNow);

        Func<Task> act = async () => await sut.HandleAsync(evt);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task HandleAsync_WhenDuplicateWithDefaultConsumerName_LogsTypeNameInSkippedMessage()
    {
        _logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);
        _inboxFilter.ExecuteIdempotentlyAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<Func<CancellationToken, ValueTask>>(),
            Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(false));

        var realHandler = new SampleOrderPlacedHandler();
        var sut = new IdempotentEventHandler<SampleOrderPlaced>(realHandler, _inboxFilter, consumerName: null, logger: _logger);
        var evt = new SampleOrderPlaced(EventId.New(), "ORD-123", DateTimeOffset.UtcNow);
        var expectedConsumer = typeof(SampleOrderPlacedHandler).FullName!;

        await sut.HandleAsync(evt);

        var logCalls = System.Linq.Enumerable.ToList(System.Linq.Enumerable.Where(_logger.ReceivedCalls(), c => c.GetMethodInfo().Name == "Log"));
        logCalls.Should().ContainSingle();
        var call = logCalls[0];
        call.GetArguments()[0].Should().Be(LogLevel.Debug);
        var logMessage = call.GetArguments()[2]?.ToString();
        logMessage.Should().NotBeNull();
        logMessage.Should().Contain($"was skipped as a duplicate by consumer '{expectedConsumer}'");
    }
}
