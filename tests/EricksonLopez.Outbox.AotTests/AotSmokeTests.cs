using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;

using EricksonLopez.Outbox.Hosting;
using EricksonLopez.Outbox.Serialization;
using EricksonLopez.Outbox.Contracts;
using EricksonLopez.Outbox.Generated;

namespace EricksonLopez.Outbox.AotTests;

public class AotSmokeTests
{
    [Fact]
    public void DI_Container_Can_Resolve_Generated_Dependencies()
    {
        var services = new ServiceCollection();
        // Just verify basic setup
        services.AddOutbox(options => 
        {
            options.UseGeneratedTypes(OutboxGeneratedJsonContext.Default);
        });

        var provider = services.BuildServiceProvider();

        // Testing that standard components can be resolved without reflection
        var resolver = provider.GetService<IOutboxMessageTypeResolver>();
    }
}

[OutboxMessage("test.message.v1")]
public class TestMessage
{
    public string Id { get; set; } = "";
}

[JsonSourceGenerationOptions(WriteIndented = false, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(TestMessage))]
public partial class OutboxGeneratedJsonContext : JsonSerializerContext { }



