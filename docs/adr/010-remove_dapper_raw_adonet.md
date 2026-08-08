# ADR 010: Removal of Dapper and Adoption of Raw ADO.NET

## Status
Accepted

## Context
Originally, the storage layer for relational databases (e.g., SQLite, MySQL, Oracle, SQL Server, PostgreSQL) relied heavily on Dapper for data access. Dapper provided a convenient and fast micro-ORM abstraction for mapping queries to objects.

However, during a comprehensive performance and architecture audit, two major limitations of Dapper became evident:
1. **Memory Allocations (Hot Paths):** Even though Dapper is highly optimized, it generates small memory allocations (e.g., arrays for parameters, mapping abstractions) on every execution. In high-throughput Outbox scenarios (thousands of messages per second), these Gen0 allocations accumulate, increasing Garbage Collection (GC) pressure and triggering "Stop-The-World" pauses.
2. **NativeAOT Compatibility:** Dapper relies on dynamic MSIL generation (`Reflection.Emit`) to build fast materializers at runtime. This approach fundamentally conflicts with NativeAOT compilation, which requires all code to be fully resolved ahead-of-time, without runtime code generation.

Furthermore, developers frequently requested an `EricksonLopez.Outbox.Dapper` integration package, assuming it was required to use Dapper in their business logic alongside the Outbox, similar to `EricksonLopez.Outbox.EntityFrameworkCore`.

## Decision
1. **Eliminate Dapper internally:** We decided to completely remove Dapper from all internal storage repositories.
2. **Adopt Raw ADO.NET:** The storage repositories were rewritten to use raw ADO.NET (`DbCommand`, `DbDataReader`, `DbParameter`). By using zero-allocation structs and avoiding boxing, we achieve zero-allocation hot paths and 100% NativeAOT compatibility.
3. **No Dapper Integration Package:** We formally decided **not** to create an `EricksonLopez.Outbox.Dapper` integration package.

## Consequences

### Positive
* **Zero-Allocation Hot Paths:** Memory allocations during `InsertAsync`, `FetchPendingAsync`, `MarkAsDispatchedAsync`, and `MarkAsFailedAsync` have been drastically reduced to near zero.
* **NativeAOT Support:** The Outbox library is now strictly AOT-compliant out of the box, as it no longer relies on dynamic code generation.
* **Architectural Simplicity:** We avoid maintaining an unnecessary integration package. Since Dapper operates as an extension on top of `IDbConnection` and utilizes standard `IDbTransaction`, users can simply pass their raw ADO.NET transactions to the `OutboxTransactionContext`. The Outbox natively understands `IDbTransaction` without requiring a dedicated wrapper, unlike Entity Framework Core which wraps transactions in `IDbContextTransaction` and requires dependency injection hooking.

### Negative
* **Verbose Internal Code:** Writing raw ADO.NET increases the verbosity of the internal storage repositories compared to Dapper's concise syntax. Maintenance of these repositories requires careful handling of raw data readers, DB nulls, and parameter creation.
* **Manual Parameter Batching:** Solving N+1 issues (e.g., for bulk inserts or updates) without Dapper requires writing custom array-binding logic (like `OracleArrayBinding`) or dynamic IN-clause generators for MySQL/SQLite.
