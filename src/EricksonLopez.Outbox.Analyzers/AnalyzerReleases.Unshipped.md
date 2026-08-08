; Unshipped analyzer releases
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
OUTBOX006 | Configuration | Error | Message type not registered for AOT JSON serialization
OUTBOX008 | Usage | Error | IOutboxMessageBuilder must be terminated with StoreAsync.
OUTBOX009 | Configuration | Warning | MaxRetryCount is 0, which disables retry logic.
OUTBOX010 | Usage | Error | StoreAsync called without a transaction in the fluent builder
OUTBOX011 | Usage | Warning | IIntegrationEvent implementer missing [OutboxMessage] attribute
OUTBOX012 | Reliability | Error | IBrokerPublisher.PublishRawAsync returns default(DispatchResult) — invalid state that dead-letters messages silently.


