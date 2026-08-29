// Copyright © Erickson Lopez. MIT License.
using System;
using EricksonLopez.Outbox.Contracts;

namespace Sample.OrderService.Domain.Aggregates.OrderAggregate;

/// <summary>
/// Domain event raised when an order is confirmed.
/// </summary>
[OutboxMessage("order.confirmed.v1")]
public sealed record OrderConfirmedEvent(
    Guid EventId,
    DateTimeOffset OccurredOn);
