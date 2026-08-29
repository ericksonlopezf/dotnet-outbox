<!-- Copyright © Erickson Lopez. MIT License. -->

# Architectural Decision Record: REJECT-005
## Rejection of Merging Outbox and Inbox into a Monolithic Messaging Package

### Status
**REJECTED (Permanent Directorial Invariant)**

### Context
Suggestions were evaluated to merge `EricksonLopez.Outbox` and `EricksonLopez.Outbox.Inbox` with `EricksonLopez.Messaging` into a single monolithic library.

### Decision
Permanently rejected. Transactional Outbox (producer reliability) and Deduplication Inbox (consumer idempotency) are orthogonal architectural patterns that can be used with relational databases, HTTP APIs, or event buses independently of any messaging broker.

### Consequences
- Modular adoption: Microservices can adopt Outbox/Inbox without referencing broker drivers (RabbitMQ/Kafka).
- Independent storage adapters for relational and document databases.
