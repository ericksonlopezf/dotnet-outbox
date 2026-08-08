using System;
using System.Diagnostics;
using AwesomeAssertions;
using EricksonLopez.Outbox.Diagnostics;
using Xunit;

namespace EricksonLopez.Outbox.Tests;

public class OutboxActivitySourceTests
{
    [Fact]
    public void StartDispatchActivity_Should_Return_Activity_When_Listener_Attached()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == OutboxActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        var parentTraceId = "00-0123456789abcdef0123456789abcdef-0123456789abcdef-01";
        
        using var activity = OutboxActivitySource.StartDispatchActivity("route", "corr-id", parentTraceId);

        activity.Should().NotBeNull();
        activity!.OperationName.Should().Be("Outbox.Dispatch");
        activity.Kind.Should().Be(ActivityKind.Producer);
        activity.GetTagItem("messaging.system").Should().Be("outbox");
        activity.GetTagItem("messaging.operation.name").Should().Be("publish");
        activity.GetTagItem("messaging.destination.name").Should().Be("route");
        // FIX-08: renamed from proprietary outbox.correlation_id to OTel standard messaging.message.conversation_id
        activity.GetTagItem("messaging.message.conversation_id").Should().Be("corr-id");
        activity.ParentId.Should().Be("00-0123456789abcdef0123456789abcdef-0123456789abcdef-01");
    }

    [Fact]
    public void StartDispatchActivity_Should_Handle_Invalid_ParentTraceId()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = s => s.Name == OutboxActivitySource.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = OutboxActivitySource.StartDispatchActivity("route", null, "invalid-trace-id");

        activity.Should().NotBeNull();
        activity!.ParentId.Should().BeNull();
    }
    
    [Fact]
    public void StartDispatchActivity_Should_Return_Null_If_No_Listeners()
    {
        using var activity = OutboxActivitySource.StartDispatchActivity("route", null, null);
        activity.Should().BeNull();
    }
}


