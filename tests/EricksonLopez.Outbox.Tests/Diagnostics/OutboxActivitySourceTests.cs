// Copyright © Erickson Lopez. MIT License.
using System;
using System.Diagnostics;
using AwesomeAssertions;
using EricksonLopez.Outbox.Diagnostics;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Diagnostics;

[Collection("ActivitySource")]
public class OutboxActivitySourceTests
{
    [Fact]
    public void StartDispatchActivity_NoListener_ReturnsNull()
    {
        // Act (without any listener listening to OutboxActivitySource)
        using var activity = OutboxActivitySource.StartDispatchActivity("order.created", null, null);

        // Assert
        activity.Should().BeNull();
    }

    [Fact]
    public void StartDispatchActivity_ListenerAttached_DefaultBrokerName_SetsAllTagsCorrectly()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == OutboxActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        var parentTraceId = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
        var parentTraceState = "rojo=1,congo=2";

        using var activity = OutboxActivitySource.StartDispatchActivity(
            messageType: "order.created.v1",
            correlationId: "corr-12345",
            parentTraceId: parentTraceId,
            parentTraceState: parentTraceState,
            messageId: "msg-99999",
            brokerSystemName: null);

        activity.Should().NotBeNull();
        activity!.OperationName.Should().Be("Outbox.Dispatch");
        activity.Kind.Should().Be(ActivityKind.Producer);
        activity.GetTagItem("messaging.system").Should().Be("outbox");
        activity.GetTagItem("messaging.operation.name").Should().Be("publish");
        activity.GetTagItem("messaging.operation.type").Should().Be("publish");
        activity.GetTagItem("messaging.destination.name").Should().Be("order.created.v1");
        activity.GetTagItem("messaging.message.id").Should().Be("msg-99999");
        activity.GetTagItem("messaging.message.conversation_id").Should().Be("corr-12345");
        activity.ParentId.Should().Be(parentTraceId);
        activity.TraceStateString.Should().Be(parentTraceState);
    }

    [Fact]
    public void StartDispatchActivity_ListenerAttached_CustomBrokerName_SetsBrokerTag()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == OutboxActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = OutboxActivitySource.StartDispatchActivity(
            messageType: "payment.processed",
            correlationId: null,
            parentTraceId: null,
            parentTraceState: null,
            messageId: null,
            brokerSystemName: "rabbitmq");

        activity.Should().NotBeNull();
        activity!.GetTagItem("messaging.system").Should().Be("rabbitmq");
        activity.GetTagItem("messaging.destination.name").Should().Be("payment.processed");
        activity.GetTagItem("messaging.message.id").Should().BeNull();
        activity.GetTagItem("messaging.message.conversation_id").Should().BeNull();
        activity.ParentId.Should().BeNull();
    }

    [Fact]
    public void StartDispatchActivity_InvalidParentTraceId_LeavesParentNull()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == OutboxActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        using var act = OutboxActivitySource.StartDispatchActivity("test", null, "invalid-trace-id");
        act.Should().NotBeNull();
        act!.ParentId.Should().BeNull();
    }

    [Fact]
    public void StartDispatchActivity_EmptyParentTraceId_LeavesParentNull()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == OutboxActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        using var act = OutboxActivitySource.StartDispatchActivity("test", null, string.Empty);
        act.Should().NotBeNull();
        act!.ParentId.Should().BeNull();
    }

    [Fact]
    public void StartStoreActivity_NoListener_ReturnsNull()
    {
        using var activity = OutboxActivitySource.StartStoreActivity("order.created", Guid.NewGuid().ToString());
        activity.Should().BeNull();
    }

    [Fact]
    public void StartStoreActivity_ListenerAttached_SetsAllTagsCorrectly()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == OutboxActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        var msgId = Guid.NewGuid().ToString();

        using var activity = OutboxActivitySource.StartStoreActivity(
            messageType: "inventory.reserved",
            messageId: msgId,
            correlationId: "corr-inv-01");

        activity.Should().NotBeNull();
        activity!.OperationName.Should().Be("Outbox.Store");
        activity.Kind.Should().Be(ActivityKind.Producer);
        activity.GetTagItem("messaging.system").Should().Be("outbox");
        activity.GetTagItem("messaging.operation.name").Should().Be("create");
        activity.GetTagItem("messaging.operation.type").Should().Be("create");
        activity.GetTagItem("messaging.destination.name").Should().Be("inventory.reserved");
        activity.GetTagItem("messaging.message.id").Should().Be(msgId);
        activity.GetTagItem("messaging.message.conversation_id").Should().Be("corr-inv-01");
    }

    [Fact]
    public void StartStoreActivity_WithoutCorrelationId_LeavesConversationIdNull()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == OutboxActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        var msgId = Guid.NewGuid().ToString();

        using var activity = OutboxActivitySource.StartStoreActivity(
            messageType: "user.registered",
            messageId: msgId,
            correlationId: null);

        activity.Should().NotBeNull();
        activity!.GetTagItem("messaging.message.id").Should().Be(msgId);
        activity.GetTagItem("messaging.message.conversation_id").Should().BeNull();
    }

    [Fact]
    public void Constants_VerifyValues()
    {
        OutboxActivitySource.SourceName.Should().Be("EricksonLopez.Outbox");
        OutboxActivitySource.OutboxSystemName.Should().Be("outbox");
        OutboxActivitySource.Source.Name.Should().Be("EricksonLopez.Outbox");
        OutboxActivitySource.Source.Version.Should().Be("2.0.0");
    }
}
