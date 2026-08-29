// Copyright © Erickson Lopez. MIT License.
using System;
using NServiceBus;

namespace EricksonLopez.Outbox.NServiceBus;

/// <summary>
/// Provides extension methods for configuring the NServiceBus outbox integration.
/// </summary>
public static class NServiceBusOutboxExtensions
{
    /// <summary>
    /// Enables the EricksonLopez.Outbox integration within the NServiceBus endpoint configuration.
    /// </summary>
    /// <param name="endpointConfiguration">The NServiceBus endpoint configuration.</param>
    /// <returns>The endpoint configuration for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="endpointConfiguration"/> is <see langword="null"/>.</exception>
    public static EndpointConfiguration EnableTransactionalOutbox(this EndpointConfiguration endpointConfiguration)
    {
        ArgumentNullException.ThrowIfNull(endpointConfiguration);

        endpointConfiguration.EnableFeature<NServiceBusOutboxFeature>();
        return endpointConfiguration;
    }
}
