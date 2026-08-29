// Copyright © Erickson Lopez. MIT License.
namespace EricksonLopez.Outbox;

/// <summary>
/// Provides well-known string constants for the EricksonLopez.Outbox library.
/// </summary>
/// <remarks>
/// Using named constants for consumer IDs prevents silent collisions between
/// the dispatcher's internal idempotency records and user-defined consumers.
/// Always pass an explicit, unique <c>consumerId</c> per consumer in your application;
/// do not reuse <see cref="DispatcherConsumerId"/> in user-facing consumers.
/// </remarks>
public static class OutboxConstants
{
    /// <summary>
    /// The consumer identifier used internally by the outbox dispatcher to track its own
    /// idempotency records via <see cref="Idempotency.IInboxIdempotencyChecker.ShouldSkipAsync"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This constant is the default value for the <c>consumerId</c> parameter of
    /// <see cref="Idempotency.IInboxIdempotencyChecker.ShouldSkipAsync"/>. It should only be used
    /// by the dispatcher infrastructure.
    /// </para>
    /// <para>
    /// <b>DO NOT</b> use this constant in your own consumers. Doing so would cause your consumer's
    /// idempotency records to collide with the dispatcher's records, resulting in incorrect
    /// duplicate-detection behavior (messages already processed by the dispatcher would appear
    /// as duplicates to your consumer, or vice versa).
    /// </para>
    /// <para>
    /// Use a stable, unique string per consumer instead, for example:
    /// <c>"order-service.payment-handler"</c>.
    /// </para>
    /// </remarks>
    public const string DispatcherConsumerId = "outbox-dispatcher";
}

