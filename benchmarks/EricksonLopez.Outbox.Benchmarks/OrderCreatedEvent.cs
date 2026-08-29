// Copyright © Erickson Lopez. MIT License.
using System;
using EricksonLopez.Outbox.Contracts;

namespace EricksonLopez.Outbox.Benchmarks;

[OutboxMessage("order.created.v1")]
public sealed record OrderCreatedEvent(Guid OrderId, decimal Total, DateTimeOffset OccurredOn);
