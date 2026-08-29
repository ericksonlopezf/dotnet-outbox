// Copyright © Erickson Lopez. MIT License.
using EricksonLopez.Outbox.Contracts;

namespace Sample.OrderService.Domain.Aggregates.OrderAggregate;

/// <summary>
/// An event with a lot of payload to showcase batching limits.
/// </summary>
[OutboxMessage("batch.test.v1")]
public sealed record BatchTestEvent(
    int Index,
    string Data);
