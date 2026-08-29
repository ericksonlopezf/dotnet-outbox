// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;

namespace EricksonLopez.Outbox.Dispatcher;

/// <summary>
/// Defines a contract that allows external notification systems (e.g., PostgreSQL LISTEN/NOTIFY)
/// to wake the dispatcher poller without creating a hard dependency on concrete poller implementations.
/// </summary>
/// <remarks>
/// Decoupling the notification listener from the concrete poller allows:
/// <list type="bullet">
///   <item>Testing the listener in isolation with a mock wakeup implementation.</item>
///   <item>Substituting the poller with future alternative implementations (e.g., priority poller).</item>
///   <item>Removing the transitive dependency of PostgreSQL storage on the core dispatcher internals.</item>
/// </list>
/// </remarks>
public interface IPollerWakeup
{
    /// <summary>
    /// Signals the poller to wake up and begin a fetch cycle immediately,
    /// bypassing the configured polling interval.
    /// </summary>
    /// <remarks>
    /// Must be safe to call from any thread and any context (e.g., a background notification callback).
    /// It must not throw under any circumstances, including if the poller has already been woken up.
    /// </remarks>
    void WakeUp();
}


