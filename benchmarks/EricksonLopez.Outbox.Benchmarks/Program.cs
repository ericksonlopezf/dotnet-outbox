// Copyright © Erickson Lopez. MIT License.
/// <summary>
/// Entry point — runs all benchmark classes and writes results to BenchmarkDotNet.Artifacts/.
/// Run with: dotnet run -c Release
/// For a single class: dotnet run -c Release -- --filter *StoreBenchmarks*
/// </summary>
/// <remarks>
/// <para>
/// <b>B-02 AUDIT FIX — Hardware metadata:</b><br/>
/// BenchmarkDotNet automatically includes the complete hardware and runtime environment
/// in every results file exported to <c>BenchmarkDotNet.Artifacts/results/</c>:
/// <list type="bullet">
///   <item>OS version (Windows 11 / Ubuntu 22.04 / macOS 14)</item>
///   <item>.NET SDK and runtime version</item>
///   <item>CPU (model, frequency, physical/logical cores)</item>
///   <item>JIT mode (RyuJIT, AOT)</item>
///   <item>Hardware intrinsics (SSE2, AVX2, etc.) via HardwareIntrinsics diagnoser</item>
/// </list>
/// </para>
/// <para>
/// <b>RULE: Never cite benchmark numbers without the hardware summary table.</b><br/>
/// When publishing results externally (README, blog posts, GitHub Discussions), always
/// include the <c>BenchmarkDotNet=...</c> environment header that appears at the top of
/// every BenchmarkDotNet report. Results without hardware context are meaningless.
/// </para>
/// <para>
/// <b>Tier 1 vs Tier 2:</b> See <c>I_CompetitorBenchmarks.cs</c> for the important
/// distinction between framework-overhead benchmarks (Tier 1, no DB I/O) and full
/// outbox benchmarks (Tier 2, real database). Do not use Tier 1 results for end-to-end
/// performance claims.
/// </para>
/// </remarks>
using System;
using System.Collections.Generic;
using System.Threading;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using EricksonLopez.Outbox;
using EricksonLopez.Outbox.Testing;
using System.Threading.Tasks;

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, DefaultConfig.Instance
    .AddJob(Job.Default.WithToolchain(InProcessEmitToolchain.Instance))
    .AddDiagnoser(MemoryDiagnoser.Default) // Default includes Gen0, Gen1, Gen2, Allocated
    // B-02 AUDIT FIX: BenchmarkDotNet natively records the complete hardware and runtime
    // environment (OS, .NET SDK, CPU model/frequency, JIT mode) in every results file
    // exported to BenchmarkDotNet.Artifacts/results/. No additional diagnoser is needed
    // for hardware context; the environment table is always emitted.
    //
    // RULE: When publishing results externally (README, blog posts, GitHub Discussions),
    // ALWAYS include the full "BenchmarkDotNet=..." environment header from the report file.
    // Results without hardware context are meaningless for cross-machine comparisons.
    //
    // AUDIT-FIX P2-C: Added RankColumn and percentile columns for competitive benchmark analysis.
    // RankColumn automatically ranks methods from fastest (1) to slowest.
    .AddColumn(RankColumn.Arabic)
    .AddColumn(StatisticColumn.OperationsPerSecond)
    .AddColumn(StatisticColumn.P50) // Median
    .AddColumn(StatisticColumn.P95)
    .AddColumn(StatisticColumn.Min)
    .AddColumn(StatisticColumn.Max));


