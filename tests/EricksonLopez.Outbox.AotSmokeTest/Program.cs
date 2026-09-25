// Copyright © Erickson Lopez. MIT License.
using System;
using EricksonLopez.Outbox.Contracts;
using EricksonLopez.Outbox.Generated;
using EricksonLopez.Outbox.Hosting;
using EricksonLopez.Outbox.Serialization;
using Microsoft.Extensions.DependencyInjection;

namespace EricksonLopez.Outbox.AotSmokeTest;

public static class Program
{
    public static int Main(string[] args)
    {
        Console.WriteLine("[AOT Smoke Test] Initializing EricksonLopez.Outbox Native AOT verification...");

        try
        {
            var services = new ServiceCollection();
            services.AddOutbox(options =>
            {
                options.UseGeneratedTypes(OutboxGeneratedJsonContext.Default);
            });

            using var provider = services.BuildServiceProvider();
            var resolver = provider.GetRequiredService<IOutboxMessageTypeResolver>();

            if (resolver == null)
            {
                Console.Error.WriteLine("[AOT Smoke Test] FAILED: Resolved IOutboxMessageTypeResolver is null.");
                return 1;
            }

            Console.WriteLine("[AOT Smoke Test] SUCCESS: EricksonLopez.Outbox Native AOT smoke test passed.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[AOT Smoke Test] EXCEPTION: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
    }
}
