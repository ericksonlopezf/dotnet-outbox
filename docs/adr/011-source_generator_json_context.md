# ADR 011: Source Generator Limitations for JsonSerializerContext

## Status
Accepted

> [!NOTE]
> This ADR expands on the decision originally documented in
> [ADR-0001](0001-limitacion-source-generator-json.md) with additional technical
> detail and planned Roslyn Analyzer mitigations.

## Context

During the architectural audit (Category 6 — Source Generators), a high-priority gap (P1) was identified: the library's source generator produces the necessary infrastructure for serialization support, but it does not automatically register message types via `[JsonSerializable]` attributes.

As a consequence, the consumer must declare a partial `JsonSerializerContext` and explicitly register each serializable type.

The audit recommended fully automating this process from the source generator.

## Decision

We decided to maintain the current design based on a consumer-declared `JsonSerializerContext` and not auto-generate `[JsonSerializable]` attributes.

To eliminate the risk of incorrect configuration, the library will incorporate a Roslyn Analyzer that validates that all message types discovered by the source generator are registered in the `JsonSerializerContext`.

The absence of any required type will constitute a compilation error, preventing assembly generation until the configuration is corrected.

## Technical Justification

Although Incremental Source Generators can produce additional code during compilation, the interaction between multiple generators within the same Roslyn pipeline does not guarantee a sufficiently deterministic information flow for this scenario.

The `System.Text.Json` source generator needs to discover a class decorated with `[JsonSerializable]` attributes to generate AOT-compatible serialization code.

Dynamically generating those attributes from another source generator introduces implicit dependencies on the execution order and phases between both generators. While this approach may work in certain scenarios, it does not constitute guaranteed or recommended behavior for a library whose goal is to offer fully deterministic Native AOT support.

Keeping the `JsonSerializerContext` as source code in the consumer project eliminates this uncertainty and ensures that `System.Text.Json` processes the context during compilation in a predictable manner.

The Analyzer complements this decision by verifying that the context registers all required types, shifting any configuration errors from runtime to the compilation process.

## Consequences

### Positive
* Guarantees fully deterministic Native AOT support.
* Avoids dependencies on the execution order of multiple source generators.
* All messages are validated during compilation.
* Configuration errors are detected before running the application.
* Reduces the risk of production incidents caused by omissions in the serialization context.

### Negative
* The consumer must explicitly declare the `JsonSerializerContext`.
* Each new message requires adding its corresponding `[JsonSerializable]` attribute.
* There is a small amount of manual configuration (boilerplate).

## Mitigation

To minimize the impact on developer experience:

* The source generator will continue generating an `OutboxJsonContext.g.cs` file with clear instructions on how to create the partial context.
* Analyzer `OUTBOX005` will verify that a configured `JsonSerializerContext` exists.
* A new analyzer (`OUTBOX006`) will validate that all messages discovered by the source generator are registered via `[JsonSerializable]`.
* The absence of any registration will produce a diagnostic with Error severity, preventing compilation until the configuration is corrected.
* The official documentation will include a complete configuration example for Native AOT.
