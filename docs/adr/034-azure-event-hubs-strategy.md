<!-- Copyright © Erickson Lopez. MIT License. -->

# ADR-034 — Azure Event Hubs Broker Strategy & AOT Compatibility

## Status

Accepted

## Context

Azure Event Hubs is a fully managed, real-time data ingestion service widely used for event streaming, telemetry pipelines, and high-throughput microservice messaging on Microsoft Azure.

While `EricksonLopez.Outbox.Brokers.AzureServiceBus` supports queue/topic messaging, large-scale streaming architectures often select Azure Event Hubs as their streaming engine.

## Decision

We introduce `EricksonLopez.Outbox.Brokers.AzureEventHubs` as a dedicated broker adapter with the following design:

1. **NativeAOT Safe Event Publishing**: Uses `Azure.Messaging.EventHubs.Producer.EventHubProducerClient`. Event payloads are pre-serialized by the library's source generators into `ReadOnlyMemory<byte>`, and wrapped in `EventData` without runtime reflection.
2. **Partitioning and Correlation**: Propagates message headers (correlation ID, causality ID, traceparent, tenant ID, and event type alias) into `EventData.Properties` and passes partition keys when specified.
3. **Resilience & DispatchResult Contract**: Implements `IBrokerPublisher` with rigorous error categorization: transient network exceptions trigger `DispatchResult.FailAndRetry(ex)`, while invalid configurations or entity not found errors yield `DispatchResult.FailFatal(ex)`.

## Rationale

1. Extends broker coverage to 8 major enterprise brokers (RabbitMQ, Kafka, Azure Service Bus, AWS SQS, GCP Pub/Sub, NATS, Redis Streams, Azure Event Hubs).
2. Maintains 100% zero-allocation/zero-reflection execution path on event ingestion and dispatching.
3. Decouples Azure SDK dependencies into an optional package without polluting core.

## Consequences

### Positive
- Direct support for high-throughput Azure Event Hubs streaming architectures.
- Full NativeAOT compatibility.

### Negative
- Additional broker package to maintain against Azure SDK minor updates.

## Related ADRs

- ADR-020: No Broker Dependency in Core
- ADR-023: Serialization Pluggable AOT First
