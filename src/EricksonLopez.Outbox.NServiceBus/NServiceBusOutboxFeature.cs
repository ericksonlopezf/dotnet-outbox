// Copyright © Erickson Lopez. MIT License.
using System;
using NServiceBus;
using NServiceBus.Features;

namespace EricksonLopez.Outbox.NServiceBus;

/// <summary>
/// Provides an NServiceBus feature that registers the transactional outbox pipeline behavior.
/// </summary>
public sealed class NServiceBusOutboxFeature : Feature
{
    /// <summary>
    /// Initializes a new instance of the <see cref="NServiceBusOutboxFeature"/> class.
    /// </summary>
    public NServiceBusOutboxFeature()
    {
        EnableByDefault();
    }

    /// <inheritdoc/>
    protected override void Setup(FeatureConfigurationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        context.Pipeline.Register(
            stepId: "EricksonLopez.Outbox.NServiceBus.OutboxPublishBehavior",
            behavior: typeof(OutboxPublishBehavior),
            description: "Persists outgoing NServiceBus messages into the transactional outbox.");
    }
}

