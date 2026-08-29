// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Events.Contracts;
using EricksonLopez.Events.Identifiers;
using EricksonLopez.Outbox.Events;
using EricksonLopez.Outbox.Persistence;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Events.Tests;

[Trait("Category", "Unit")]
public sealed class OutboxEventPublisherTests
{
    private readonly IOutbox _outbox = Substitute.For<IOutbox>();
    private readonly IOutboxTransactionProvider _transactionProvider = Substitute.For<IOutboxTransactionProvider>();
    private readonly IOutboxTransactionContext _txContext = Substitute.For<IOutboxTransactionContext>();

    private sealed record OrderPlacedEvent(string OrderId, decimal Amount) : IIntegrationEvent
    {
        public EventId Id { get; init; } = EventId.New();
        public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
    }

    [Fact]
    public void Constructor_WhenOutboxNull_ThrowsArgumentNullException()
    {
        var act = () => new OutboxEventPublisher(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("outbox");
    }

    [Fact]
    public async Task Constructor_WhenTransactionProviderOmitted_UsesNullTransactionProvider_AndThrowsOnPublish()
    {
        var sut = new OutboxEventPublisher(_outbox);
        var evt = new OrderPlacedEvent("ORD-DEFAULT", 50.0m);

        Func<Task> act = async () => await sut.PublishAsync(evt);

        var thrown = await act.Should().ThrowExactlyAsync<InvalidOperationException>();
        thrown.WithMessage($"Cannot store event '{nameof(OrderPlacedEvent)}' ({evt.Id}) into the outbox because no active transaction context was provided by '{nameof(NullOutboxTransactionProvider)}'.");
    }

    [Fact]
    public async Task PublishAsync_WhenEventNull_ThrowsArgumentNullException()
    {
        var sut = new OutboxEventPublisher(_outbox, _transactionProvider);
        Func<Task> act = async () => await sut.PublishAsync<OrderPlacedEvent>(null!);
        await act.Should().ThrowExactlyAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task PublishAsync_WhenNoTransactionContext_ThrowsInvalidOperationException()
    {
        _transactionProvider.CurrentTransaction.Returns((IOutboxTransactionContext?)null);
        var sut = new OutboxEventPublisher(_outbox, _transactionProvider);
        var evt = new OrderPlacedEvent("ORD-123", 99.99m);

        Func<Task> act = async () => await sut.PublishAsync(evt);

        var thrown = await act.Should().ThrowExactlyAsync<InvalidOperationException>();
        thrown.WithMessage($"Cannot store event '{nameof(OrderPlacedEvent)}' ({evt.Id}) into the outbox because no active transaction context was provided by '{_transactionProvider.GetType().Name}'.");
    }

    [Fact]
    public async Task PublishAsync_WithCancelledToken_ThrowsOperationCanceledException()
    {
        var sut = new OutboxEventPublisher(_outbox, _transactionProvider);
        var evt = new OrderPlacedEvent("ORD-CANCELLED", 10.0m);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = async () => await sut.PublishAsync(evt, cts.Token);

        await act.Should().ThrowExactlyAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task PublishAsync_WithCancelledToken_EvenIfTransactionContextNull_ThrowsOperationCanceledExceptionBeforeCheckingTransaction()
    {
        _transactionProvider.CurrentTransaction.Returns((IOutboxTransactionContext?)null);
        var sut = new OutboxEventPublisher(_outbox, _transactionProvider);
        var evt = new OrderPlacedEvent("ORD-CANCELLED-NOTX", 10.0m);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = async () => await sut.PublishAsync(evt, cts.Token);

        await act.Should().ThrowExactlyAsync<OperationCanceledException>();
        _outbox.ReceivedCalls().Should().BeEmpty();
    }

    [Fact]
    public async Task PublishAsync_WhenTransactionContextActive_StoresEventInOutbox()
    {
        _transactionProvider.CurrentTransaction.Returns(_txContext);
        var sut = new OutboxEventPublisher(_outbox, _transactionProvider);
        var evt = new OrderPlacedEvent("ORD-123", 99.99m);
        using var cts = new CancellationTokenSource();

        await sut.PublishAsync(evt, cts.Token);

        await _outbox.Received(1).StoreAsync(
            evt,
            _txContext,
            Arg.Is<OutboxMessageMetadata>(m => m.MessageType == typeof(OrderPlacedEvent).FullName),
            null,
            cts.Token);
    }
}
