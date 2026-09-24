// Copyright © Erickson Lopez. MIT License.
using System.Diagnostics.Metrics;
using EricksonLopez.Outbox.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace EricksonLopez.Outbox;

/// <summary>
/// Provides internal service collection helpers for outbox diagnostics.
/// </summary>
internal static class OutboxServiceCollectionInternals
{
    internal static void AddOutboxDiagnostics(IServiceCollection services)
    {
        services.TryAddSingleton(sp => new OutboxMetrics(sp.GetService<IMeterFactory>()));
        services.TryAddSingleton<IErrorSanitizer, DefaultErrorSanitizer>();
    }
}
