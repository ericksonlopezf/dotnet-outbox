# ADR-001: Monorepo Modular Structure

## 1. Title and Status
**Self-Contained Modular Structure (Monorepo with 6 Projects)**
*Status:* ~~Approved and Implemented~~ → **SUPERSEDED by [ADR-009](009-package_consolidation_strategy.md)**

> [!WARNING]
> This ADR describes the original consolidated package model (6 projects).
> The repository has since migrated to **per-provider packages** (17 source projects)
> as documented in ADR-009. This ADR is retained for historical context only.

## 2. Context and Motivation
The `EricksonLopez.Outbox` framework aims to become the gold standard for transactional messaging in .NET. However, a distributed ecosystem naturally tends to explode into dozens of small NuGet packages (e.g., `Core`, `Contracts`, `PostgreSQL`, `SqlServer`, `RabbitMQ`, `Kafka`, etc.).
The motivation behind this decision is to avoid "Dependency Hell" and developer fatigue (DX) when having to install multiple abstractions. The solution requires smart packaging: a single unified Core and consolidated "Provider" projects (`Storage`, `Brokers`), maintaining zero coupling through strict internal folders.

## 3. Evaluated Alternatives
1. **Micro-Projects (1 Project = 1 NuGet Package):** Create 14+ projects (one for PostgreSQL, one for RabbitMQ, one for Retry, one for Idempotency).
2. **Absolute Monolith:** Everything (Core + PostgreSQL + RabbitMQ) in a single giant assembly.

## 4. Advantages
* **Consolidation:** The user only needs to install `EricksonLopez.Outbox` and `EricksonLopez.Outbox.Storage`.
* **Fast Deployment:** Fewer `.dll` files and fewer assembly jumps in memory.
* **DDD Organization:** Physical separation via self-contained folders (`/Storage/PostgreSql`, `/Storage/SqlServer`) allows multiple implementations to coexist in one assembly without intertwining references.

## 5. Disadvantages
* **Shared Third-Party Dependencies:** The `Storage` project requires referencing `Npgsql`, `Microsoft.Data.SqlClient`, etc. If a user only uses PostgreSQL, they will still download SQL Server binaries (though the AOT compiler and Linker will Trim them out since they are unused).

## 6. Trade-offs
We accept including multiple database SDKs in the `Storage` project in exchange for drastically simplifying package distribution. This is an acceptable trade-off thanks to the maturity of Trimming in .NET 10, which will discard any uninvoked SQL driver statically.

## 7. Performance Impact
* **Improvement:** Reduction in "Assembly Loading" cost during application startup, as the CLR only needs to resolve 6 assemblies instead of 15.

## 8. NativeAOT Impact
* **Neutral/Positive:** The AOT Compiler (ILC) analyzes the static graph. Having fewer assembly boundaries allows for more aggressive cross-assembly inlining and Dead Code Elimination.

## 9. Maintainability Impact
* **Improvement:** Pull requests will not require editing multiple `.csproj` files or syncing cross-versions of internal dependencies.

## 10. Extensibility Impact
* **Neutral:** To add a new broker (e.g., Kafka), one simply adds a `Brokers/Kafka` folder inside `EricksonLopez.Outbox.Brokers`, implementing `IBrokerPublisher`.

## 11. Developer Experience (DX) Impact
* **Excellent:** The user configures their Outbox with a fluent syntax without having to track down which obscure NuGet package to download for Idempotency or Metrics support. Everything comes *batteries included* but remains strictly modular.
