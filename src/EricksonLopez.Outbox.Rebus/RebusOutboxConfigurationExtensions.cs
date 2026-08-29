// Copyright © Erickson Lopez. MIT License.
using System;
using Rebus.Config;
using Rebus.Pipeline;
using Rebus.Pipeline.Send;

namespace EricksonLopez.Outbox.Rebus;

/// <summary>
/// Provides configuration extension methods for integrating the outbox with Rebus.
/// </summary>
public static class RebusOutboxConfigurationExtensions
{
    /// <summary>
    /// Configures Rebus pipeline to use the transactional outbox step.
    /// </summary>
    /// <param name="configurer">The options configurer.</param>
    /// <param name="outbox">The outbox instance.</param>
    /// <exception cref="ArgumentNullException"><paramref name="configurer"/> or <paramref name="outbox"/> is <see langword="null"/>.</exception>
    public static void EnableTransactionalOutbox(this OptionsConfigurer configurer, IOutbox outbox)
    {
        ArgumentNullException.ThrowIfNull(configurer);
        ArgumentNullException.ThrowIfNull(outbox);

        configurer.Decorate<IPipeline>(c =>
        {
            var pipeline = c.Get<IPipeline>();
            var step = new OutboxOutgoingStep(outbox);
            return new PipelineStepInjector(pipeline)
                .OnSend(step, PipelineRelativePosition.Before, typeof(SendOutgoingMessageStep));
        });
    }
}

