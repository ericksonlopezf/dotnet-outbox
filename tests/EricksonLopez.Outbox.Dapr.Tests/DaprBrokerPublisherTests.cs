// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using Dapr.Client;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Dapr;
using EricksonLopez.Result;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.Dapr.Tests;

public class DaprBrokerPublisherTests
{
    [Fact]
    public void Constructor_NullDaprClient_ThrowsArgumentNullException()
    {
        var act = () => new DaprBrokerPublisher(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("daprClient");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Constructor_DefaultPubsubName_WhenNullOrWhitespace(string? pubsubName)
    {
        var client = Substitute.For<DaprClient>();
        var publisher = new DaprBrokerPublisher(client, pubsubName!);

        var payload = Encoding.UTF8.GetBytes("{\"id\":1}");
        var message = new OutboxMessage(
            Guid.NewGuid(), "test.topic", payload, null, null,
            ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null,
            OutboxMessageStatus.Pending, 0, null);

        var metadata = new OutboxMessageMetadata(null, null, "test.topic", Array.Empty<MetadataEntry>());
        var context = new DispatchContext(CancellationToken.None, 1);

        var result = await publisher.PublishRawAsync(message, metadata, context);
        result.Success.Should().BeTrue();

        await client.Received(1).PublishEventAsync(
            "pubsub",
            "test.topic",
            Arg.Any<object>(),
            Arg.Any<Dictionary<string, string>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void BrokerSystemName_ReturnsDapr()
    {
        var client = Substitute.For<DaprClient>();
        var publisher = new DaprBrokerPublisher(client);

        publisher.BrokerSystemName.Should().Be("dapr");
    }

    [Fact]
    public async Task PublishRawAsync_NullMessage_ThrowsArgumentNullException()
    {
        var client = Substitute.For<DaprClient>();
        var publisher = new DaprBrokerPublisher(client);
        var metadata = new OutboxMessageMetadata(null, null, "test", Array.Empty<MetadataEntry>());
        var context = new DispatchContext(CancellationToken.None, 1);

        Func<Task> act = async () => await publisher.PublishRawAsync(null!, metadata, context);
        (await act.Should().ThrowAsync<ArgumentNullException>()).WithParameterName("message");
    }

    [Fact]
    public async Task PublishRawAsync_ValidMessage_PublishesEventToDaprWithAllMetadata()
    {
        var client = Substitute.For<DaprClient>();
        var publisher = new DaprBrokerPublisher(client, "my-pubsub");
        using var cts = new CancellationTokenSource();

        var payload = Encoding.UTF8.GetBytes("{\"orderId\":\"12345\"}");
        var message = new OutboxMessage(
            Guid.NewGuid(),
            "orders.created",
            payload,
            "corr-1",
            "caus-1",
            ReadOnlyMemory<byte>.Empty,
            DateTimeOffset.UtcNow,
            null,
            null,
            OutboxMessageStatus.Pending,
            0,
            null);

        var metadata = new OutboxMessageMetadata("corr-1", "caus-1", "orders.created", new[] { new MetadataEntry("customKey", "customVal") });
        var context = new DispatchContext(cts.Token, attempt: 1);

        var result = await publisher.PublishRawAsync(message, metadata, context);

        result.Success.Should().BeTrue();
        await client.Received(1).PublishEventAsync(
            "my-pubsub",
            "orders.created",
            Arg.Any<object>(),
            Arg.Is<Dictionary<string, string>>(d =>
                d["correlationId"] == "corr-1" &&
                d["causationId"] == "caus-1" &&
                d["customKey"] == "customVal"),
            cts.Token);
    }

    [Fact]
    public async Task PublishRawAsync_EmptyMetadata_DoesNotIncludeCorrelationOrCausationKeys()
    {
        var client = Substitute.For<DaprClient>();
        var publisher = new DaprBrokerPublisher(client, "my-pubsub");

        var payload = Encoding.UTF8.GetBytes("{\"orderId\":\"12345\"}");
        var message = new OutboxMessage(
            Guid.NewGuid(),
            "orders.created",
            payload,
            null,
            null,
            ReadOnlyMemory<byte>.Empty,
            DateTimeOffset.UtcNow,
            null,
            null,
            OutboxMessageStatus.Pending,
            0,
            null);

        var metadata = new OutboxMessageMetadata(null, null, "orders.created", Array.Empty<MetadataEntry>());
        var context = new DispatchContext(CancellationToken.None, attempt: 1);

        var result = await publisher.PublishRawAsync(message, metadata, context);

        result.Success.Should().BeTrue();
        await client.Received(1).PublishEventAsync(
            "my-pubsub",
            "orders.created",
            Arg.Any<object>(),
            Arg.Is<Dictionary<string, string>>(d =>
                !d.ContainsKey("correlationId") &&
                !d.ContainsKey("causationId")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishRawAsync_CancellationRequested_RethrowsOperationCanceledException()
    {
        var client = Substitute.For<DaprClient>();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        client.PublishEventAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<object>(),
            Arg.Any<Dictionary<string, string>>(),
            Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new OperationCanceledException(cts.Token));

        var publisher = new DaprBrokerPublisher(client, "my-pubsub");

        var payload = Encoding.UTF8.GetBytes("{\"orderId\":\"12345\"}");
        var message = new OutboxMessage(
            Guid.NewGuid(), "orders.created", payload, null, null,
            ReadOnlyMemory<byte>.Empty, DateTimeOffset.UtcNow, null, null,
            OutboxMessageStatus.Pending, 0, null);

        var metadata = new OutboxMessageMetadata(null, null, "orders.created", Array.Empty<MetadataEntry>());
        var context = new DispatchContext(cts.Token, attempt: 1);

        Func<Task> act = async () => await publisher.PublishRawAsync(message, metadata, context);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task PublishRawAsync_DaprThrowsException_ReturnsFailAndRetry()
    {
        var client = Substitute.For<DaprClient>();
        client.PublishEventAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<object>(),
            Arg.Any<Dictionary<string, string>>(),
            Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("Dapr sidecar unavailable"));

        var publisher = new DaprBrokerPublisher(client, "my-pubsub");

        var payload = Encoding.UTF8.GetBytes("{\"orderId\":\"12345\"}");
        var message = new OutboxMessage(
            Guid.NewGuid(),
            "orders.created",
            payload,
            "corr-1",
            "caus-1",
            ReadOnlyMemory<byte>.Empty,
            DateTimeOffset.UtcNow,
            null,
            null,
            OutboxMessageStatus.Pending,
            0,
            null);

        var metadata = new OutboxMessageMetadata("corr-1", "caus-1", "orders.created", Array.Empty<MetadataEntry>());
        var context = new DispatchContext(CancellationToken.None, attempt: 1);

        var result = await publisher.PublishRawAsync(message, metadata, context);

        result.Success.Should().BeFalse();
        result.ShouldRetry.Should().BeTrue();
    }

    [Fact]
    public void AddDaprBrokerPublisher_NullServices_ThrowsArgumentNullException()
    {
        Action act = () => DaprOutboxExtensions.AddDaprBrokerPublisher(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("services");
    }

    [Fact]
    public void AddDaprBrokerPublisher_RegistersServiceInContainer()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<DaprClient>());
        services.AddDaprBrokerPublisher("custom-pubsub");

        var sp = services.BuildServiceProvider();
        var publisher = sp.GetService<IBrokerPublisher>();

        publisher.Should().NotBeNull();
        publisher.Should().BeOfType<DaprBrokerPublisher>();
    }

    [Fact]
    public void AddDaprBrokerPublisher_WithDefaultPubsubName_RegistersServiceInContainer()
    {
        var services = new ServiceCollection();
        services.AddSingleton(Substitute.For<DaprClient>());
        services.AddDaprBrokerPublisher();

        var sp = services.BuildServiceProvider();
        var publisher = sp.GetService<IBrokerPublisher>();

        publisher.Should().NotBeNull();
        publisher.Should().BeOfType<DaprBrokerPublisher>();
    }

    [Fact]
    public void UseDapr_NullOptions_ThrowsArgumentNullException()
    {
        Action act = () => DaprOutboxExtensions.UseDapr(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("options");
    }

    [Fact]
    public void UseDapr_ConfiguresBrokerFactory()
    {
        var services = new ServiceCollection();
        var options = (OutboxOptions)Activator.CreateInstance(typeof(OutboxOptions), BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { services }, null)!;
        var returned = options.UseDapr("custom-pubsub");

        returned.Should().BeSameAs(options);

        services.AddSingleton(Substitute.For<DaprClient>());
        var sp = services.BuildServiceProvider();

        var factoryProp = typeof(OutboxOptions).GetProperty("DefaultPublisherFactory", BindingFlags.NonPublic | BindingFlags.Instance);
        var factory = (Func<IServiceProvider, IBrokerPublisher>?)factoryProp!.GetValue(options);
        factory.Should().NotBeNull();
        var publisher = factory!(sp);
        publisher.Should().NotBeNull();
        publisher.Should().BeOfType<DaprBrokerPublisher>();
    }

    [Fact]
    public void UseDapr_WithDefaultPubsubName_ConfiguresBrokerFactory()
    {
        var services = new ServiceCollection();
        var options = (OutboxOptions)Activator.CreateInstance(typeof(OutboxOptions), BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { services }, null)!;
        var returned = options.UseDapr();

        returned.Should().BeSameAs(options);

        services.AddSingleton(Substitute.For<DaprClient>());
        var sp = services.BuildServiceProvider();

        var factoryProp = typeof(OutboxOptions).GetProperty("DefaultPublisherFactory", BindingFlags.NonPublic | BindingFlags.Instance);
        var factory = (Func<IServiceProvider, IBrokerPublisher>?)factoryProp!.GetValue(options);
        factory.Should().NotBeNull();
        var publisher = factory!(sp);
        publisher.Should().NotBeNull();
        publisher.Should().BeOfType<DaprBrokerPublisher>();
    }
}

