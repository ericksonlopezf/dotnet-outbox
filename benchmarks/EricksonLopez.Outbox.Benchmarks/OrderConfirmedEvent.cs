// Copyright © Erickson Lopez. MIT License.
using System;
using EricksonLopez.Outbox.Contracts;

namespace EricksonLopez.Outbox.Benchmarks;

[OutboxMessage("order.confirmed.v1")]
public sealed record OrderConfirmedEvent(Guid OrderId, DateTimeOffset ConfirmedAt);
