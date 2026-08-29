// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Outbox.MultiTenancy;

/// <summary>
/// Defines a contract for tenant-aware destination broker and topic or queue routing.
/// </summary>
public interface ITenantBrokerRouter
{
    /// <summary>
    /// Determines the destination topic or queue name for a message belonging to a specific tenant.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="baseDestination">The standard or configured base topic/queue name.</param>
    /// <param name="messageType">The message type alias.</param>
    /// <returns>The tenant-specific routing destination.</returns>
    string ResolveDestination(string? tenantId, string baseDestination, string messageType);
}



