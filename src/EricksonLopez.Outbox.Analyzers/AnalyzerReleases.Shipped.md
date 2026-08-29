; Copyright © Erickson Lopez. MIT License.
; Shipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 1.0.0

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
OUTBOX001 | Design | Error | Message type missing 'Guid Id' property required for outbox identification.
OUTBOX002 | Usage | Error | Type passed to IOutbox is missing [OutboxMessage] attribute; NativeAOT serialization will fail at runtime.
OUTBOX003 | Reliability | Warning | Consumer is not decorated with [InboxConsumer]; duplicate messages can cause side-effect duplication.
OUTBOX004 | Configuration | Warning | Potentially infinite retry configuration detected (MaxAttempts <= 0 or > 100).
OUTBOX005 | Configuration | Error | OutboxOptions must configure a valid IOutboxSerializer for NativeAOT support.
OUTBOX006 | Usage | Warning | Type implements IIntegrationEvent but is missing [OutboxMessage] attribute; NativeAOT type resolver will throw KeyNotFoundException at runtime.
OUTBOX007 | Reliability | Warning | StoreAsync called without a transaction; the Transactional Outbox pattern requires the write to share the business DB transaction for atomicity.
