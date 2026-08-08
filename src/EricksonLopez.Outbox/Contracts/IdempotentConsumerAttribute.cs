using System;

namespace EricksonLopez.Outbox.Contracts;

/// <summary>
/// Marks a consumer class as intentionally idempotent, suppressing the OUTBOX003 analyzer warning.
/// </summary>
/// <remarks>
/// Apply this attribute to message consumers (e.g. MediatR <c>IRequestHandler</c>) 
/// to explicitly declare that the consumer has been implemented in an idempotent manner,
/// such as performing its own deduplication or utilizing inherently idempotent operations.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class IdempotentConsumerAttribute : Attribute
{
}
