# EricksonLopez.Outbox

[![NuGet](https://img.shields.io/nuget/v/EricksonLopez.Outbox?style=for-the-badge&logo=nuget&logoColor=white&color=512BD4)](https://www.nuget.org/packages/EricksonLopez.Outbox)
[![NuGet Downloads](https://img.shields.io/nuget/dt/EricksonLopez.Outbox?style=for-the-badge&logo=nuget&logoColor=white&color=004880)](https://www.nuget.org/packages/EricksonLopez.Outbox)
[![CI](https://img.shields.io/github/actions/workflow/status/ericksonlopezf/dotnet-outbox/ci.yml?branch=main&style=for-the-badge&logo=githubactions&logoColor=white&label=CI)](https://github.com/ericksonlopezf/dotnet-outbox/actions)
[![Coverage](https://img.shields.io/codecov/c/github/ericksonlopezf/dotnet-outbox?style=for-the-badge&logo=codecov&logoColor=white)](https://codecov.io/gh/ericksonlopezf/dotnet-outbox)
[![Mutation Score](https://img.shields.io/badge/Mutation_Score-%E2%89%A598%25-brightgreen?style=for-the-badge&logo=stryker&logoColor=white)](docs/quality-gates.md)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg?style=for-the-badge)](LICENSE)
[![.NET](https://img.shields.io/badge/.NET_8_%7C_9_%7C_10-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com)
[![NativeAOT](https://img.shields.io/badge/NativeAOT-Compatible-brightgreen?style=for-the-badge)](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot)

A high-performance, cloud-native, and zero-allocation oriented implementation of the **Transactional Outbox** and **Idempotent Inbox** patterns for .NET.

The **Transactional Outbox** pattern ensures that the creation or modification of a domain model and the publishing of the corresponding event to a Message Broker (e.g., RabbitMQ, Kafka) are performed as an **atomic** operation, avoiding the dreaded _"Dual Write Problem"_.

The **Idempotent Inbox** pattern protects your consumers from executing business logic twice in the event that the Message Broker delivers the same message more than once (At-Least-Once Delivery).

## ⚡ Performance

> BenchmarkDotNet v0.13.12 · .NET 10.0.10 (10.0.1026.32716) · X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI · Windows 11 (10.0.26200.8875)
>
> Storage: InMemory (isolates framework CPU/GC overhead from network I/O). See [full methodology](docs/benchmark-results.md#methodology).

### vs. Industry Competitors — `StoreAsync` (single message)

| Method | Mean | Error | StdDev | Ratio | Allocated | Alloc Ratio |
|---|---:|---:|---:|---:|---:|---:|
| **EricksonLopez.Outbox** | **256 ns** | **±1.4 ns** | **±1.3 ns** | **1.0×** | **448 B** | **1.0×** |
| CAP `StoreAsync` | 856 ns | ±7.3 ns | ±6.5 ns | 3.3× | 1,664 B | 3.7× |
| NServiceBus `StoreAsync` | 25,424 ns | ±194 ns | ±181 ns | 99× | 5,457 B | 12.2× |

### Serialization — `IBufferWriter<byte>` vs allocating path

| Method | Payload | Mean | Ratio | Allocated | Alloc Ratio |
|---|---:|---:|---:|---:|---:|
| `Serialize_BufferWriter` | 512 B | 54 ns | **0.68×** | **32 B** | **0.05×** |
| `Serialize_Allocating` | 512 B | 79 ns | 1.0× | 592 B | 1.0× |
| `Serialize_BufferWriter` | 10 KB | 337 ns | **0.57×** | **32 B** | **0.003×** |
| `Serialize_Allocating` | 10 KB | 593 ns | 1.0× | 10,320 B | 1.0× |
| `Serialize_BufferWriter` | 100 KB | 3,380 ns | **0.44×** | **32 B** | **~0×** |
| `Serialize_Allocating` | 100 KB | 7,767 ns | 1.0× | 102,573 B | 1.0× |

### Concurrency — Parallel `StoreAsync` (linear scaling up to 64 threads)

| Threads | Mean | Ops/s | Allocated |
|---:|---:|---:|---:|
| 1 | 847 ns | 1,181,111 | 728 B |
| 4 | 1,546 ns | 646,999 | 2,600 B |
| 16 | 4,475 ns | 223,472 | 9,800 B |
| 64 | 9,700 ns | 103,097 | 38,601 B |

### Type Resolution (FrozenDictionary — O(1) zero-alloc)

| Method | Mean | Allocated |
|---|---:|---:|
| `GetAlias` | **1.37 ns** | **0 B** |
| `Resolve` | **2.59 ns** | **0 B** |

**Key takeaways:**
- **3.3× faster** and **73% less memory** than CAP in store operations.
- **99× faster** and **92% less memory** than NServiceBus.
- `IBufferWriter<byte>` serialization path allocates only **32 B** regardless of payload size — a **94–99.97% memory reduction** vs the allocating path.
- Type resolution is **zero-allocation** at **~1–2 nanoseconds** via `FrozenDictionary`.
- Scales linearly to **64 concurrent threads** with **zero lock contention** on the store path.

→ [Full benchmark results and methodology](docs/benchmark-results.md) · [Performance tuning guide](docs/performance-guide.md)

## Key Features

*   **Guaranteed Atomicity:** Seamless integration with ADO.NET transactions (`DbTransactionContext`) and Entity Framework Core.
*   **Extreme Performance:** Optimized with `ReadOnlyMemory<T>`, `ValueTask`, and array pooling for serialization to reduce Gen 0 garbage collection.
*   **AOT Ready:** Native support for Ahead-Of-Time (Native AOT) compilation via `System.Text.Json` Source Generators. Zero runtime reflection.
*   **Adaptive Dispatcher:** Dynamic polling that reduces frequency when there is no load (Adaptive Polling) to prevent database saturation, using `SKIP LOCKED` for multi-instance horizontal scalability.
*   **Circuit Breaker & Retry Policies:** Robust fault tolerance strategies using `ExponentialBackoffPolicy`.
*   **Built-in Idempotency:** Native de-duplication of incoming messages via the Inbox daemon.
*   **Broker Abstraction:** Decoupled from the transport layer. Send messages to RabbitMQ, Kafka, Azure Service Bus, AWS SQS, Google Pub/Sub, NATS, or Redis Streams.

## Ecosystem Packages

| Package | Description | NuGet |
|---------|-------------|-------|
| `EricksonLopez.Outbox` | Core interfaces, dispatcher, and base logic | [![NuGet](https://img.shields.io/nuget/v/EricksonLopez.Outbox.svg)](https://www.nuget.org/packages/EricksonLopez.Outbox) |
| `EricksonLopez.Outbox.EntityFrameworkCore` | Native integration for EF Core | [![NuGet](https://img.shields.io/nuget/v/EricksonLopez.Outbox.EntityFrameworkCore.svg)](https://www.nuget.org/packages/EricksonLopez.Outbox.EntityFrameworkCore) |
| `EricksonLopez.Outbox.MassTransit` | MassTransit integration | [![NuGet](https://img.shields.io/nuget/v/EricksonLopez.Outbox.MassTransit.svg)](https://www.nuget.org/packages/EricksonLopez.Outbox.MassTransit) |
| `EricksonLopez.Outbox.Storage.PostgreSql` | PostgreSQL native storage provider | [![NuGet](https://img.shields.io/nuget/v/EricksonLopez.Outbox.Storage.PostgreSql.svg)](https://www.nuget.org/packages/EricksonLopez.Outbox.Storage.PostgreSql) |
| `EricksonLopez.Outbox.Storage.SqlServer` | SQL Server native storage provider | [![NuGet](https://img.shields.io/nuget/v/EricksonLopez.Outbox.Storage.SqlServer.svg)](https://www.nuget.org/packages/EricksonLopez.Outbox.Storage.SqlServer) |
| `EricksonLopez.Outbox.Storage.MySql` | MySQL native storage provider | [![NuGet](https://img.shields.io/nuget/v/EricksonLopez.Outbox.Storage.MySql.svg)](https://www.nuget.org/packages/EricksonLopez.Outbox.Storage.MySql) |
| `EricksonLopez.Outbox.Storage.Oracle` | Oracle native storage provider | [![NuGet](https://img.shields.io/nuget/v/EricksonLopez.Outbox.Storage.Oracle.svg)](https://www.nuget.org/packages/EricksonLopez.Outbox.Storage.Oracle) |
| `EricksonLopez.Outbox.Storage.Sqlite` | SQLite native storage provider | [![NuGet](https://img.shields.io/nuget/v/EricksonLopez.Outbox.Storage.Sqlite.svg)](https://www.nuget.org/packages/EricksonLopez.Outbox.Storage.Sqlite) |
| `EricksonLopez.Outbox.Brokers.RabbitMQ` | RabbitMQ physical publisher | [![NuGet](https://img.shields.io/nuget/v/EricksonLopez.Outbox.Brokers.RabbitMQ.svg)](https://www.nuget.org/packages/EricksonLopez.Outbox.Brokers.RabbitMQ) |
| `EricksonLopez.Outbox.Brokers.Kafka` | Kafka physical publisher | [![NuGet](https://img.shields.io/nuget/v/EricksonLopez.Outbox.Brokers.Kafka.svg)](https://www.nuget.org/packages/EricksonLopez.Outbox.Brokers.Kafka) |
| `EricksonLopez.Outbox.Brokers.AzureServiceBus` | Azure Service Bus publisher | [![NuGet](https://img.shields.io/nuget/v/EricksonLopez.Outbox.Brokers.AzureServiceBus.svg)](https://www.nuget.org/packages/EricksonLopez.Outbox.Brokers.AzureServiceBus) |
| `EricksonLopez.Outbox.Brokers.AwsSqs` | AWS SQS physical publisher | [![NuGet](https://img.shields.io/nuget/v/EricksonLopez.Outbox.Brokers.AwsSqs.svg)](https://www.nuget.org/packages/EricksonLopez.Outbox.Brokers.AwsSqs) |
| `EricksonLopez.Outbox.Brokers.GooglePubSub` | Google Pub/Sub publisher | [![NuGet](https://img.shields.io/nuget/v/EricksonLopez.Outbox.Brokers.GooglePubSub.svg)](https://www.nuget.org/packages/EricksonLopez.Outbox.Brokers.GooglePubSub) |
| `EricksonLopez.Outbox.Brokers.Nats` | NATS physical publisher | [![NuGet](https://img.shields.io/nuget/v/EricksonLopez.Outbox.Brokers.Nats.svg)](https://www.nuget.org/packages/EricksonLopez.Outbox.Brokers.Nats) |
| `EricksonLopez.Outbox.Brokers.RedisStreams` | Redis Streams publisher | [![NuGet](https://img.shields.io/nuget/v/EricksonLopez.Outbox.Brokers.RedisStreams.svg)](https://www.nuget.org/packages/EricksonLopez.Outbox.Brokers.RedisStreams) |
| `EricksonLopez.Outbox.SourceGenerators` | Compile-time type mapping generator | [![NuGet](https://img.shields.io/nuget/v/EricksonLopez.Outbox.SourceGenerators.svg)](https://www.nuget.org/packages/EricksonLopez.Outbox.SourceGenerators) |
| `EricksonLopez.Outbox.Analyzers` | Roslyn analyzers for correct outbox usage | [![NuGet](https://img.shields.io/nuget/v/EricksonLopez.Outbox.Analyzers.svg)](https://www.nuget.org/packages/EricksonLopez.Outbox.Analyzers) |

## .NET Framework Support Policy

All library packages target `net8.0`, `net9.0`, and `net10.0`. Analyzers and SourceGenerators target `netstandard2.0`.

> **Support Policy**: This library supports only .NET frameworks with **active official support from Microsoft**. A framework version is included in `TargetFrameworks` as long as it appears on the [Microsoft .NET Support Policy page](https://dotnet.microsoft.com/platform/support/policy/dotnet-core) under **Active** or **Maintenance** status. Framework versions are removed from `TargetFrameworks` when they reach their official end-of-life date as defined by Microsoft — not before, and not after.
>
> | Framework | Type | Microsoft Support End Date | Status |
> |---|---|---|---|
> | .NET 8 | LTS | November 10, 2026 | ✅ Supported |
> | .NET 9 | STS | **November 10, 2026** | ✅ Supported |
> | .NET 10 | LTS | November 2028 | ✅ Supported |

## Quick Start

1. Install the core package and a storage provider:
    ```bash
    dotnet add package EricksonLopez.Outbox
    dotnet add package EricksonLopez.Outbox.Storage.PostgreSql
    ```
2. Configure services in your `Program.cs`:
    ```csharp
    builder.Services.AddOutbox(options =>
    {
        // 1. Use Source Generators for JSON serialization
        options.UseSerializer(new NativeAotJsonSerializer(OutboxJsonContext.Default));
        // 2. Resolve type mapping for AOT
        options.UseGeneratedTypes(); 
    });

    // 3. Register the Database Repository
    builder.Services.AddScoped<IOutboxRepository, PostgreSqlOutboxRepository>();

    // 4. Start the Background Daemons
    builder.Services.AddOutboxDispatcher(options =>
    {
        options.BatchSize = 100;
        options.UseAdaptivePolling = true;
    });
    ```

## Documentation

| Topic | Link |
|-------|------|
| Architecture & Flows | [docs/architecture.md](docs/architecture.md) |
| API Reference | [docs/api-reference.md](docs/api-reference.md) |
| Cookbook & Best Practices | [docs/cookbook.md](docs/cookbook.md) |
| Progressive Tutorial | [docs/showcase/](docs/showcase/) |
| Packages & Versioning | [docs/packages.md](docs/packages.md) |
| Compatibility Matrix | [docs/compatibility-matrix.md](docs/compatibility-matrix.md) |
| Performance & Benchmarks | [docs/benchmark-results.md](docs/benchmark-results.md) |
| Performance Tuning Guide | [docs/performance-guide.md](docs/performance-guide.md) |
| CI/CD Pipeline | [docs/ci-cd.md](docs/ci-cd.md) |
| Quality Gates | [docs/quality-gates.md](docs/quality-gates.md) |
| Migration Guide | [docs/migration-guide.md](docs/migration-guide.md) |
| Troubleshooting & FAQ | [docs/troubleshooting.md](docs/troubleshooting.md) |
| Design Decisions (ADRs) | [docs/design-decisions.md](docs/design-decisions.md) |
| Comparative Analysis | [docs/comparative-analysis.md](docs/comparative-analysis.md) |
| Repository Inventory | [docs/repository-inventory.md](docs/repository-inventory.md) |

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for information on building the project, running tests, and contributing guidelines.

## Security

Please review [SECURITY.md](SECURITY.md) for details on our security policies and how to report vulnerabilities.

## Code of Conduct

We follow the Contributor Covenant. See [CODE_OF_CONDUCT.md](CODE_OF_CONDUCT.md) for details.

## License

This project is licensed under the MIT License — see the [LICENSE](LICENSE) file for details.
