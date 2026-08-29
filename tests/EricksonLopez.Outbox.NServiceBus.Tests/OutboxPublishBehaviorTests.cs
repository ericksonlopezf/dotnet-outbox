// Copyright © Erickson Lopez. MIT License.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AwesomeAssertions;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.NServiceBus;
using EricksonLopez.Outbox.Persistence;
using NServiceBus;
using NServiceBus.Extensibility;
using NServiceBus.Features;
using NServiceBus.Pipeline;
using NSubstitute;
using Xunit;

namespace EricksonLopez.Outbox.NServiceBus.Tests;

public class OutboxPublishBehaviorTests
{
    public sealed record SampleNServiceBusMessage(string Content);

    [Fact]
    public void Constructor_NullOutbox_ThrowsArgumentNullException()
    {
        var act = () => new OutboxPublishBehavior(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("outbox");
    }

    [Fact]
    public async Task Invoke_NullParameters_ThrowsArgumentNullException()
    {
        var outbox = Substitute.For<IOutbox>();
        var behavior = new OutboxPublishBehavior(outbox);

        Func<Task> next = () => Task.CompletedTask;
        var act1 = () => behavior.Invoke(null!, next);
        (await act1.Should().ThrowAsync<ArgumentNullException>()).WithParameterName("context");

        var context = Substitute.For<IOutgoingLogicalMessageContext>();
        var act2 = () => behavior.Invoke(context, (Func<Task>)null!);
        (await act2.Should().ThrowAsync<ArgumentNullException>()).WithParameterName("next");
    }

    [Fact]
    public async Task Invoke_WithTransactionContext_StoresInOutboxAndCallsNext()
    {
        var outbox = Substitute.For<IOutbox>();
        var behavior = new OutboxPublishBehavior(outbox);
        using var cts = new CancellationTokenSource();

        var txContext = Substitute.For<IOutboxTransactionContext>();
        var message = new SampleNServiceBusMessage("order-123");

        var logicalMessage = new OutgoingLogicalMessage(typeof(SampleNServiceBusMessage), message);

        var context = Substitute.For<IOutgoingLogicalMessageContext>();
        context.Message.Returns(logicalMessage);
        context.CancellationToken.Returns(cts.Token);

        var contextBag = new ContextBag();
        contextBag.Set<IOutboxTransactionContext>(txContext);
        context.Extensions.Returns(contextBag);

        bool nextCalled = false;
        Func<Task> next = () =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        await behavior.Invoke(context, next);

        nextCalled.Should().BeTrue();
        await outbox.Received(1).StoreAsync(message, txContext, cts.Token);
    }

    [Fact]
    public async Task Invoke_WithTransactionContextButNullMessageInstance_CallsNextWithoutStoring()
    {
        var outbox = Substitute.For<IOutbox>();
        var behavior = new OutboxPublishBehavior(outbox);

        var txContext = Substitute.For<IOutboxTransactionContext>();

        var logicalMessage = (OutgoingLogicalMessage)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(OutgoingLogicalMessage));

        var context = Substitute.For<IOutgoingLogicalMessageContext>();
        context.Message.Returns(logicalMessage);
        context.CancellationToken.Returns(CancellationToken.None);

        var contextBag = new ContextBag();
        contextBag.Set<IOutboxTransactionContext>(txContext);
        context.Extensions.Returns(contextBag);

        bool nextCalled = false;
        Func<Task> next = () =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        await behavior.Invoke(context, next);

        nextCalled.Should().BeTrue();
        await outbox.DidNotReceive().StoreAsync(Arg.Any<object>(), Arg.Any<IOutboxTransactionContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_WithoutTransactionContext_CallsNextWithoutStoring()
    {
        var outbox = Substitute.For<IOutbox>();
        var behavior = new OutboxPublishBehavior(outbox);

        var message = new SampleNServiceBusMessage("order-123");
        var logicalMessage = new OutgoingLogicalMessage(typeof(SampleNServiceBusMessage), message);

        var context = Substitute.For<IOutgoingLogicalMessageContext>();
        context.Message.Returns(logicalMessage);
        context.CancellationToken.Returns(CancellationToken.None);
        context.Extensions.Returns(new ContextBag());

        bool nextCalled = false;
        Func<Task> next = () =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        await behavior.Invoke(context, next);

        nextCalled.Should().BeTrue();
        await outbox.DidNotReceive().StoreAsync(Arg.Any<object>(), Arg.Any<IOutboxTransactionContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Invoke_WithNullTransactionContextInBag_CallsNextWithoutStoring()
    {
        var outbox = Substitute.For<IOutbox>();
        var behavior = new OutboxPublishBehavior(outbox);

        var message = new SampleNServiceBusMessage("order-123");
        var logicalMessage = new OutgoingLogicalMessage(typeof(SampleNServiceBusMessage), message);

        var context = Substitute.For<IOutgoingLogicalMessageContext>();
        context.Message.Returns(logicalMessage);
        context.CancellationToken.Returns(CancellationToken.None);

        var contextBag = new ContextBag();
        contextBag.Set<IOutboxTransactionContext>(null!);
        context.Extensions.Returns(contextBag);

        bool nextCalled = false;
        Func<Task> next = () =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        };

        await behavior.Invoke(context, next);

        nextCalled.Should().BeTrue();
        await outbox.DidNotReceive().StoreAsync(Arg.Any<object>(), Arg.Any<IOutboxTransactionContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void NServiceBusOutboxFeature_Constructor_Should_EnableByDefault()
    {
        var feature = new NServiceBusOutboxFeature();
        var prop = typeof(Feature).GetProperty("IsEnabledByDefault", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        var isEnabled = (bool)prop!.GetValue(feature)!;
        isEnabled.Should().BeTrue();
    }

    [Fact]
    public void EnableTransactionalOutbox_NullConfiguration_ThrowsArgumentNullException()
    {
        var act = () => NServiceBusOutboxExtensions.EnableTransactionalOutbox(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("endpointConfiguration");
    }

    [Fact]
    public void EnableTransactionalOutbox_ValidConfiguration_EnablesFeature()
    {
        var config = new EndpointConfiguration("TestEndpoint");
        var returned = config.EnableTransactionalOutbox();

        returned.Should().BeSameAs(config);

        var settingsField = typeof(EndpointConfiguration).BaseType!.GetField("<Settings>k__BackingField", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var settingsHolder = settingsField.GetValue(config)!;
        
        var overridesProp = settingsHolder.GetType().GetField("Overrides", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public)!;
        var defaultsProp = settingsHolder.GetType().GetField("Defaults", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public)!;
        
        var overrides = (System.Collections.IDictionary)overridesProp.GetValue(settingsHolder)!;
        var defaults = (System.Collections.IDictionary)defaultsProp.GetValue(settingsHolder)!;

        bool found = false;
        foreach (var dict in new[] { overrides, defaults })
        {
            foreach (System.Collections.DictionaryEntry entry in dict)
            {
                if (entry.Value is System.Collections.IEnumerable enumerable && !(entry.Value is string))
                {
                    foreach (var item in enumerable)
                    {
                        if (item is Type t && t == typeof(NServiceBusOutboxFeature))
                        {
                            found = true;
                            break;
                        }
                    }
                }
                else if (entry.Key?.ToString()?.Contains("NServiceBusOutboxFeature") == true)
                {
                    found = true;
                    break;
                }
            }
        }

        found.Should().BeTrue("NServiceBusOutboxFeature must be registered in endpoint configuration settings");
    }

    [Fact]
    public void NServiceBusOutboxFeature_Setup_ValidContext_RegistersPublishBehavior()
    {
        var feature = new NServiceBusOutboxFeature();
        var setupMethod = typeof(NServiceBusOutboxFeature).GetMethod("Setup", BindingFlags.NonPublic | BindingFlags.Instance)!;

        var context = (FeatureConfigurationContext)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(FeatureConfigurationContext));
        var settingsType = typeof(PipelineSettings).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public)[0].GetParameters()[0].ParameterType;
        var settings = Activator.CreateInstance(settingsType, true)!;
        var pipeline = (PipelineSettings)Activator.CreateInstance(typeof(PipelineSettings), BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public, null, new object[] { settings }, null)!;

        var pipelineField = typeof(FeatureConfigurationContext).GetField("pipeline", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? typeof(FeatureConfigurationContext).GetFields(BindingFlags.NonPublic | BindingFlags.Instance).First(f => f.FieldType == typeof(PipelineSettings));

        pipelineField.SetValue(context, pipeline);

        setupMethod.Invoke(feature, new object[] { context });

        var modsField = typeof(PipelineSettings).GetField("modifications", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var mods = modsField.GetValue(pipeline)!;

        var additionsMember = (System.Collections.IEnumerable?)(mods.GetType().GetProperty("Additions", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public)?.GetValue(mods)
            ?? mods.GetType().GetField("additions", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(mods));
        
        additionsMember.Should().NotBeNull();

        bool registered = false;
        foreach (var step in additionsMember!)
        {
            var stepIdProp = step.GetType().GetProperty("StepId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                ?? step.GetType().GetProperty("Id", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var behaviorProp = step.GetType().GetProperty("BehaviorType", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                ?? step.GetType().GetProperty("Behavior", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                ?? step.GetType().GetProperty("Type", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

            var stepId = stepIdProp?.GetValue(step)?.ToString();
            var behavior = behaviorProp?.GetValue(step) as Type;

            if (stepId == "EricksonLopez.Outbox.NServiceBus.OutboxPublishBehavior" || behavior == typeof(OutboxPublishBehavior))
            {
                registered = true;
                break;
            }
        }

        registered.Should().BeTrue("OutboxPublishBehavior must be registered in Additions list of PipelineModifications");
    }

    [Fact]
    public void NServiceBusOutboxFeature_Setup_NullContext_ThrowsArgumentNullException()
    {
        var feature = new NServiceBusOutboxFeature();
        var setupMethod = typeof(NServiceBusOutboxFeature).GetMethod("Setup", BindingFlags.NonPublic | BindingFlags.Instance);
        
        Action act = () =>
        {
            try
            {
                setupMethod!.Invoke(feature, new object[] { null! });
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                throw ex.InnerException;
            }
        };

        act.Should().Throw<ArgumentNullException>().WithParameterName("context");
    }
}
