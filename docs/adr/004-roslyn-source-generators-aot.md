<!-- Copyright © Erickson Lopez. MIT License. -->

# ADR-004: Metaprogramming with Incremental Source Generators

## 1. Title and Status
**Attribute Scanning (`[OutboxMessage]`) at Compile-Time without Reflection**
*Status:* Approved and Implemented in `EricksonLopez.Outbox.SourceGenerators` and `EricksonLopez.Outbox.Analyzers`.

## 2. Context and Motivation
Traditionally, .NET libraries scan assemblies at startup using `System.Reflection` to find types marked with an attribute or to register serialization mappings.
With the advent of **NativeAOT** (Ahead-Of-Time Compilation), runtime Reflection is partially banned because the compiler trims code it believes won't be used. If we rely on Reflection, the AOT program will throw fatal exceptions in production.
The motivation is to guarantee mandatory `IsAotCompatible = true` compliance so the Outbox can run in 20MB memory Docker containers in microseconds.

## 3. Evaluated Alternatives
1. **Exhaustive Manual Registration:** Forcing the developer to manually map every event (e.g., `options.Map<OrderCreated>("order.v1");`). It is fail-proof but the Developer Experience (DX) is awful; users forget to map events and fail in production.
2. **Classic Reflection (`Assembly.GetTypes()`):** Destroys NativeAOT compatibility, slows down API startup (Cold Start penalty).
3. **Incremental Source Generators (Roslyn):** Scanning the AST of the source code while the developer types in their IDE and generating invisible static classes.

## 4. Advantages
* **Immediate Startup Performance:** Zero CPU cost at runtime. All mapping was already resolved at compile time.
* **100% AOT Safe:** By generating direct calls (e.g., `JsonSerializerContext.Default`), the ILc compiler knows exactly which types to protect against Trimming.
* **Strict Security (Shift-Left):** Thanks to the `OutboxMessageAnalyzer`, errors are caught in the IDE with a red squiggly line under the code (Error OUTBOX001) months before reaching production.

## 5. Disadvantages
* **Internal Complexity Curve:** Maintaining Source Generators is notoriously difficult due to the Roslyn API.
* **IDE Performance:** A poorly written Source Generator can freeze Visual Studio.

## 6. Trade-offs
We invested in exclusively using the `IIncrementalGenerator` API (Roslyn V4+) instead of the obsolete `ISourceGenerator`. The incremental engine ensures the analysis only runs on code nodes the user just typed, keeping the IDE fluid, in exchange for significantly higher internal technical complexity.

## 7. Performance Impact
* **Improvement:** Eliminates the classic "Cold Start Penalty" seen in older libraries like MassTransit or MediatR.

## 8. NativeAOT Impact
* **Essential:** This architectural decision is the pillar that allows the library to call itself "NativeAOT Ready". Without Source Generators, AOT would not be possible without a destructive DX.

## 9. Extensibility Impact
* **Positive:** Allows injecting additional behavior into user messages invisibly (e.g., static injection of default constructors for deserialization).

## 10. Developer Experience (DX) Impact
* **Extraordinary:** The user just annotates their record `[OutboxMessage("order.created")]` and magic happens. If they forget, the analyzer gently penalizes them on the spot.
