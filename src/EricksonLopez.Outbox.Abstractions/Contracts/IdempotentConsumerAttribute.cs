// Copyright © Erickson Lopez. MIT License.
using System;

namespace EricksonLopez.Outbox.Contracts;

/// <summary>
/// Marks a consumer class as intentionally idempotent, suppressing the OUTBOX003 analyzer warning.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class IdempotentConsumerAttribute : Attribute
{
}
