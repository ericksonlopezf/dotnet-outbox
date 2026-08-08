namespace EricksonLopez.Outbox;

/// <summary>
/// Represents the current processing status of an outbox message, as stored in the database.
/// </summary>
/// <remarks>
/// State machine transitions:
/// <list type="bullet">
///   <item><description><see cref="Pending"/> (0) → <see cref="InFlight"/> (1): FetchPendingAsync claims the message.</description></item>
///   <item><description><see cref="InFlight"/> (1) → DELETE: MarkAsDispatchedAsync removes the row on success (default: <c>DeleteOnDispatch = true</c>).</description></item>
///   <item><description><see cref="InFlight"/> (1) → <see cref="Dispatched"/> (2): MarkAsDispatchedAsync soft-updates when <c>DeleteOnDispatch = false</c> (audit trail mode).</description></item>
///   <item><description><see cref="InFlight"/> (1) → <see cref="Failed"/> (3): MarkAsFailedAsync on transient failure.</description></item>
///   <item><description><see cref="Failed"/> (3) → <see cref="InFlight"/> (1): FetchPendingAsync retries after deliver_at elapses.</description></item>
///   <item><description><see cref="InFlight"/> (1) → <see cref="DeadLettered"/> (4): MarkAsFailedAsync after MaxRetryCount exceeded.</description></item>
///   <item><description><see cref="InFlight"/> (1) → <see cref="Pending"/> (0): ReclaimStaleMessagesAsync resets stale messages.</description></item>
/// </list>
///
/// <para>
/// <b>Important</b>: Under default configuration (<c>DeleteOnDispatch = true</c>), value 2 is never
/// written to the database — successfully dispatched messages are physically deleted to eliminate
/// MVCC bloat. Value 2 (<see cref="Dispatched"/>) only appears when <c>DeleteOnDispatch = false</c>
/// is configured for audit trail or compliance scenarios.
/// </para>
/// </remarks>
public enum OutboxMessageStatus
{
    /// <summary>
    /// The message is queued and ready to be dispatched.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// The message has been claimed by a dispatcher and is currently being processed.
    /// This state prevents duplicate processing across concurrent dispatcher instances.
    /// </summary>
    InFlight = 1,

    /// <summary>
    /// The message was successfully dispatched to the broker.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Delete-on-Dispatch mode (default, <c>OutboxRuntimeOptions.DeleteOnDispatch = true</c>):</b><br/>
    /// This state value is <b>never written to the database</b>. Dispatched messages are
    /// physically deleted from the outbox table, eliminating MVCC bloat in PostgreSQL.
    /// A row with <c>state = 2</c> should never appear in the database under default configuration.
    /// </para>
    /// <para>
    /// <b>Soft-delete mode (<c>OutboxRuntimeOptions.DeleteOnDispatch = false</c>):</b><br/>
    /// When soft-delete is enabled for audit trails or compliance requirements,
    /// dispatched messages are updated to <c>state = 2</c> and remain in the table.
    /// The <c>processed_at</c> column is populated with the dispatch timestamp.
    /// </para>
    /// </remarks>
    Dispatched = 2,


    /// <summary>
    /// The message failed to dispatch and is scheduled for retry.
    /// The <c>deliver_at</c> column determines when the next retry attempt will occur (exponential backoff).
    /// </summary>
    Failed = 3,

    /// <summary>
    /// The message has exhausted all retry attempts and has been moved to the dead-letter queue.
    /// No further dispatch attempts will be made. Manual intervention is required.
    /// </summary>
    DeadLettered = 4
}
