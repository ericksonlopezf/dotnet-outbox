using EricksonLopez.Outbox.Contracts;

namespace Sample.OrderService.Domain.Aggregates.OrderAggregate;

/// <summary>
/// Domain event raised when a new order is placed.
/// Note the `OutboxMessage` attribute which sets the MessageType for the broker.
/// </summary>
[OutboxMessage("order.created.v1")]
public sealed record OrderCreatedEvent(
    Guid EventId,
    string CustomerId,
    decimal Total,
    DateTimeOffset OccurredOn) : IIntegrationEvent;

/// <summary>
/// Domain event raised when an order is confirmed.
/// </summary>
[OutboxMessage("order.confirmed.v1")]
public sealed record OrderConfirmedEvent(
    Guid EventId,
    DateTimeOffset OccurredOn) : IIntegrationEvent;

/// <summary>
/// An event with a lot of payload to showcase batching limits.
/// </summary>
[OutboxMessage("batch.test.v1")]
public sealed record BatchTestEvent(
    int Index,
    string Data);
