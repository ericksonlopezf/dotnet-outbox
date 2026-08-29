// Copyright © Erickson Lopez. MIT License.
using System;
using EricksonLopez.Outbox.Contracts;

namespace EricksonLopez.Outbox.Benchmarks;

[OutboxMessage("variable.payload.v1")]
public sealed record VariablePayloadEvent(Guid Id, string Data);
