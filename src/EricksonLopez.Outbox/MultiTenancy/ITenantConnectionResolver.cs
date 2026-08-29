// Copyright © Erickson Lopez. MIT License.
using System;
using System.Threading;
using System.Threading.Tasks;

namespace EricksonLopez.Outbox.MultiTenancy;

/// <summary>
/// Defines a contract for resolving tenant-specific database connection strings or schemas for partitioned multi-tenancy.
/// </summary>
public interface ITenantConnectionResolver
{
    /// <summary>
    /// Resolves the database connection string or schema identifier for the given tenant.
    /// </summary>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
    /// <returns>A task that represents the asynchronous resolution, returning the tenant connection string or target identifier.</returns>
    ValueTask<string> ResolveConnectionStringAsync(string tenantId, CancellationToken cancellationToken = default);
}



