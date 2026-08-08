# EricksonLopez.Outbox — Benchmarks

Contains the performance benchmarks for EricksonLopez.Outbox, including direct comparisons with MassTransit, CAP, NServiceBus, and Wolverine.

## Quick Start

```bash
# Requires: dotnet 10 SDK, Release mode
cd benchmarks/EricksonLopez.Outbox.Benchmarks
dotnet run -c Release
```

Select the benchmark when prompted, or run all with `--filter *`.

## Run Individual Benchmarks

```bash
# All benchmarks
dotnet run -c Release -- --filter *

# Only serialization benchmarks
dotnet run -c Release -- --filter *Serialization*

# Only competitive benchmarks (comparison with MassTransit, CAP, NServiceBus)
dotnet run -c Release -- --filter *Competitor*

# Only storage benchmarks (comparison with MassTransit InMemory, Wolverine)
dotnet run -c Release -- --filter *Store*

# Quick smoke test (no full warmup, for CI)
dotnet run -c Release -- --filter * --job Dry
```

## Benchmarks Description

| File | Class | Description |
|---|---|---|
| `A_SerializationBenchmarks.cs` | `A_SerializationBenchmarks` | STJ serialization with/without `IBufferWriter<byte>`. Compares allocating vs zero-copy path. |
| `B_MessageConstructionBenchmarks.cs` | `B_MessageConstructionBenchmarks` | Cost of constructing an `OutboxMessage` with the fluent builder. |
| `C_StoreBenchmarks.cs` | `C_StoreBenchmarks` | **Competitive**: EricksonLopez.Outbox vs MassTransit InMemory vs Wolverine. Measures `StoreAsync` / `Publish` with InMemory backends. |
| `D_BatchStoreBenchmarks.cs` | `D_BatchStoreBenchmarks` | Batch insert: UNNEST-based bulk insert vs individual inserts. |
| `E_TypeResolutionBenchmarks.cs` | `E_TypeResolutionBenchmarks` | Type resolution by alias: `FrozenDictionary` vs `ImmutableDictionary` vs conventional dict. |
| `F_EndToEndBenchmarks.cs` | `F_EndToEndBenchmarks` | Full pipeline: store → channel → dispatch → mark dispatched (InMemory). |
| `G_PipelineBenchmarks.cs` | `G_PipelineBenchmarks` | Cost of the middleware pipeline with 0, 1, and N middlewares. |
| `H_SqlFetchBenchmarks.cs` | `H_SqlFetchBenchmarks` | `FetchPendingAsync` with real PostgreSQL (requires `PG_BENCH_DSN` env var). |
| `I_CompetitorBenchmarks.cs` | `I_CompetitorBenchmarks` | **Competitive**: EricksonLopez.Outbox vs CAP vs NServiceBus `StoreAsync`. |
| `J_ParallelBenchmarks.cs` | `J_ParallelBenchmarks` | Throughput under parallel load: multiple concurrent producers. |
| `K_ThroughputBenchmarks.cs` | `K_ThroughputBenchmarks` | Sustained messages/sec throughput with InMemory backend. |

## Methodology

- **Runtime**: .NET 10.0, Release mode with `/O2`
- **Toolchain**: `InProcessEmitToolchain` to reduce process variance
- **Warmup**: 20 iterations (configurable per benchmark)
- **Iterations**: 100 iterations minimum (configurable per benchmark)
- **Columns**: `Mean`, `Allocated`, `Gen0`, `Gen1`, `Gen2`, `Op/s`, `P50`, `P95`, `Min`, `Max`
- **Reference Machine**: See `BenchmarkDotNet.Artifacts/results/*.json` for hardware specs

## Interpreting Results

### What does each metric measure?

| Metric | Description | Goal |
|---|---|---|
| `Mean` | Average time per operation | Lower = better |
| `Allocated` | Bytes allocated per operation on the heap (Gen0+) | Lower = better. 0 = allocation-free |
| `Gen0` | Gen0 collections per 1000 operations | Lower = better |
| `Op/s` | Operations per second | Higher = better |
| `P95` | 95th percentile latency | Lower = better for tail latency |
| `Ratio` | Compared to the baseline marked `[Baseline=true]` | <1.0 = faster than baseline |

### Key Expectations

| Benchmark | Expectation |
|---|---|
| `A_SerializationBenchmarks` | BufferWriter path must have `Allocated = 0` vs the baseline path that allocates a `byte[]` |
| `C_StoreBenchmarks` | EricksonLopez must be competitive with MassTransit. Any difference <2x is acceptable since EricksonLopez includes builder overhead |
| `E_TypeResolutionBenchmarks` | `FrozenDictionary` must beat `ImmutableDictionary` by ~30% in throughput |
| `I_CompetitorBenchmarks` | EricksonLopez must beat CAP in allocations (zero-reflection vs reflection-heavy) |

## Real PostgreSQL Benchmarks

The `H_SqlFetchBenchmarks` benchmarks require an accessible PostgreSQL instance.

```bash
# Configure connection string
$env:PG_BENCH_DSN = "Host=localhost;Database=outbox_bench;Username=bench;Password=bench"

# Run only SQL benchmarks
dotnet run -c Release -- --filter *Sql*
```

The benchmark table is created automatically if it doesn't exist.

## Result Artifacts

Results are saved to `BenchmarkDotNet.Artifacts/results/`:
- `*.md` — Markdown result tables (to include in README and PRs)
- `*.json` — Full results with hardware metadata
- `*.csv` — For Excel/Sheets analysis

## Contributing Benchmarks

When adding a new benchmark:
1. Prefix with the next available letter (`L_`, `M_`, etc.)
2. Always add `[MemoryDiagnoser]`
3. Explicitly mark the baseline with `[Benchmark(Baseline = true)]`
4. For competitive benchmarks, ensure the competitor setup is equivalent (same hardware, same configuration)
5. Update this table in the README

## Reference Hardware (Last Run)

> Results vary by hardware. For reproducible comparisons, run on the same machine
> or in CI with fixed hardware.

See `BenchmarkDotNet.Artifacts/results/` for the results of the last run with hardware specs.
