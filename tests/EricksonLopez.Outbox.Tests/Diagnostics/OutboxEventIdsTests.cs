// Copyright © Erickson Lopez. MIT License.
using System.Reflection;
using AwesomeAssertions;
using EricksonLopez.Outbox.Diagnostics;
using Microsoft.Extensions.Logging;
using Xunit;

namespace EricksonLopez.Outbox.Tests.Diagnostics;

public class OutboxEventIdsTests
{
    [Fact]
    public void OutboxEventIds_AllPublicStaticReadonlyFields_AreNonNullAndHaveValidIdsAndNames()
    {
        var fields = typeof(OutboxEventIds).GetFields(BindingFlags.Public | BindingFlags.Static);
        fields.Should().NotBeEmpty();

        foreach (var field in fields)
        {
            field.FieldType.Should().Be<EventId>();
            var value = (EventId)field.GetValue(null)!;
            value.Id.Should().BeGreaterThan(0, because: $"field {field.Name} must have a positive EventId");
            value.Name.Should().NotBeNullOrWhiteSpace(because: $"field {field.Name} must have a valid Name");
            value.Name.Should().Be(field.Name, because: "EventId name should match field name");
        }
    }

    [Fact]
    public void OutboxEventIds_SpecificEventIds_HaveExpectedValues()
    {
        OutboxEventIds.MessageDispatched.Id.Should().Be(10000);
        OutboxEventIds.MessageDispatchFailed.Id.Should().Be(10001);
        OutboxEventIds.MessageDeadLettered.Id.Should().Be(10002);
        OutboxEventIds.DlqInsertFailed.Id.Should().Be(10003);
        OutboxEventIds.MessageRetried.Id.Should().Be(10004);
        OutboxEventIds.ChannelCancelled.Id.Should().Be(10005);
        OutboxEventIds.PayloadTooLarge.Id.Should().Be(10006);
        OutboxEventIds.HeadersTooLarge.Id.Should().Be(10007);
        OutboxEventIds.HeadersDeserializeFailed.Id.Should().Be(10008);
        OutboxEventIds.MessageDelayedNoRetry.Id.Should().Be(10009);
        OutboxEventIds.DbRetryAttempt.Id.Should().Be(10010);
        OutboxEventIds.InvalidDispatchResultDetected.Id.Should().Be(10011);
        OutboxEventIds.DlqPayloadFallback.Id.Should().Be(10012);

        OutboxEventIds.StartupValidationFailed.Id.Should().Be(10100);
        OutboxEventIds.StartupValidationPassed.Id.Should().Be(10101);
        OutboxEventIds.ProducerOnlyMode.Id.Should().Be(10102);
        OutboxEventIds.DispatcherStarting.Id.Should().Be(10103);
        OutboxEventIds.DispatcherStopped.Id.Should().Be(10104);
        OutboxEventIds.DispatcherConsumerCrashed.Id.Should().Be(10105);
        OutboxEventIds.DispatcherConsumerStarted.Id.Should().Be(10106);
        OutboxEventIds.CircuitBreakerTripped.Id.Should().Be(10107);
        OutboxEventIds.CircuitBreakerReset.Id.Should().Be(10108);
        OutboxEventIds.CircuitBreakerHalfOpen.Id.Should().Be(10109);

        OutboxEventIds.PollerStarted.Id.Should().Be(10200);
        OutboxEventIds.PollerStopped.Id.Should().Be(10201);
        OutboxEventIds.PollerError.Id.Should().Be(10202);
        OutboxEventIds.BatchFetched.Id.Should().Be(10203);

        OutboxEventIds.InboxCleanupStarted.Id.Should().Be(10300);
        OutboxEventIds.InboxCleanupPurged.Id.Should().Be(10301);
        OutboxEventIds.InboxCleanupError.Id.Should().Be(10302);
        OutboxEventIds.InboxDuplicateDetected.Id.Should().Be(10303);
    }
}
