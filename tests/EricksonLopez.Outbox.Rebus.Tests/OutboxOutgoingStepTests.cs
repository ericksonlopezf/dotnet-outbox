// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Persistence;
using EricksonLopez.Outbox.Rebus;
using NSubstitute;
using Rebus.Messages;
using Rebus.Pipeline;
using Rebus.Pipeline.Send;
using Rebus.Transport;
using Xunit;

namespace EricksonLopez.Outbox.Rebus.Tests;

public class OutboxOutgoingStepTests
{
    private static readonly string[] TestDestination = ["test-queue"];

    public sealed record RebusOrderMessage(string OrderId);

    [Fact]
    public void Constructor_NullOutbox_ThrowsArgumentNullException()
    {
        var act = () => new OutboxOutgoingStep(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("outbox");
    }

    [Fact]
    public async Task Process_NullParameters_ThrowsArgumentNullException()
    {
        var outbox = Substitute.For<IOutbox>();
        var step = new OutboxOutgoingStep(outbox);

        var act1 = () => step.Process(null!, () => Task.CompletedTask);
        (await act1.Should().ThrowAsync<ArgumentNullException>()).WithParameterName("context");

        var context = new OutgoingStepContext(
            new Message(new Dictionary<string, string>(), new RebusOrderMessage("123")),
            Substitute.For<ITransactionContext>(),
            new DestinationAddresses(TestDestination));

        var act2 = () => step.Process(context, null!);
        (await act2.Should().ThrowAsync<ArgumentNullException>()).WithParameterName("next");
    }

    [Fact]
    public async Task Process_WithTransactionContext_StoresInOutboxAndCallsNext()
    {
        var outbox = Substitute.For<IOutbox>();
        var step = new OutboxOutgoingStep(outbox);

        var txContext = Substitute.For<IOutboxTransactionContext>();
        var body = new RebusOrderMessage("order-456");
        var message = new Message(new Dictionary<string, string>(), body);

        var rebusTx = Substitute.For<ITransactionContext>();
        var items = new System.Collections.Concurrent.ConcurrentDictionary<string, object>();
        rebusTx.Items.Returns(items);

        var context = new OutgoingStepContext(
            message,
            rebusTx,
            new DestinationAddresses(TestDestination));

        context.Save(txContext);

        bool nextCalled = false;
        await step.Process(context, () =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        nextCalled.Should().BeTrue();
        await outbox.Received(1).StoreAsync(body, txContext);
    }

    [Fact]
    public async Task Process_WithTransactionContextButNullMessageBody_CallsNextWithoutStoring()
    {
        var outbox = Substitute.For<IOutbox>();
        var step = new OutboxOutgoingStep(outbox);

        var txContext = Substitute.For<IOutboxTransactionContext>();
        var message = (Message)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(Message));

        var rebusTx = Substitute.For<ITransactionContext>();
        var items = new System.Collections.Concurrent.ConcurrentDictionary<string, object>();
        rebusTx.Items.Returns(items);

        var context = new OutgoingStepContext(
            message,
            rebusTx,
            new DestinationAddresses(TestDestination));

        context.Save(txContext);

        bool nextCalled = false;
        await step.Process(context, () =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        nextCalled.Should().BeTrue();
        await outbox.DidNotReceive().StoreAsync(Arg.Any<object>(), Arg.Any<IOutboxTransactionContext>());
    }

    [Fact]
    public async Task Process_WithoutTransactionContext_CallsNextWithoutStoring()
    {
        var outbox = Substitute.For<IOutbox>();
        var step = new OutboxOutgoingStep(outbox);

        var body = new RebusOrderMessage("order-456");
        var message = new Message(new Dictionary<string, string>(), body);

        var rebusTx = Substitute.For<ITransactionContext>();
        var items = new System.Collections.Concurrent.ConcurrentDictionary<string, object>();
        rebusTx.Items.Returns(items);

        var context = new OutgoingStepContext(
            message,
            rebusTx,
            new DestinationAddresses(TestDestination));

        bool nextCalled = false;
        await step.Process(context, () =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        nextCalled.Should().BeTrue();
        await outbox.DidNotReceive().StoreAsync(Arg.Any<object>(), Arg.Any<IOutboxTransactionContext>());
    }

    [Fact]
    public void EnableTransactionalOutbox_NullParameters_ThrowsArgumentNullException()
    {
        var configurer = (global::Rebus.Config.OptionsConfigurer)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(global::Rebus.Config.OptionsConfigurer));
        var outbox = Substitute.For<IOutbox>();

        var act1 = () => RebusOutboxConfigurationExtensions.EnableTransactionalOutbox(null!, outbox);
        act1.Should().Throw<ArgumentNullException>().WithParameterName("configurer");

        var act2 = () => RebusOutboxConfigurationExtensions.EnableTransactionalOutbox(configurer, null!);
        act2.Should().Throw<ArgumentNullException>().WithParameterName("outbox");
    }

    [Fact]
    public void EnableTransactionalOutbox_DecoratesPipeline()
    {
        var injectionist = new global::Rebus.Injection.Injectionist();
        var options = (global::Rebus.Config.Options)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(global::Rebus.Config.Options));
        var configurer = (global::Rebus.Config.OptionsConfigurer)Activator.CreateInstance(typeof(global::Rebus.Config.OptionsConfigurer), BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance, null, new object[] { options, injectionist }, null)!;
        var outbox = Substitute.For<IOutbox>();

        var rawPipeline = Substitute.For<global::Rebus.Pipeline.IPipeline>();
        injectionist.Register<global::Rebus.Pipeline.IPipeline>(_ => rawPipeline);

        configurer.EnableTransactionalOutbox(outbox);

        var resolution = injectionist.Get<global::Rebus.Pipeline.IPipeline>();
        var resolved = resolution.GetType().GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(resolution)
                    ?? resolution.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(resolution);

        resolved.Should().NotBeNull();
        resolved.Should().NotBeSameAs(rawPipeline);
        resolved.Should().BeOfType<global::Rebus.Pipeline.PipelineStepInjector>();
    }
}



