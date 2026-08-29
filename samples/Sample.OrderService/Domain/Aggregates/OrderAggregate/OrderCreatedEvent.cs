// Copyright © Erickson Lopez. MIT License.
using System;
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
    DateTimeOffset OccurredOn);
