<!-- Copyright © Erickson Lopez. MIT License. -->

```

BenchmarkDotNet v0.13.12, Windows 11 (10.0.26200.8875)
Unknown processor
.NET SDK 10.0.302
  [Host]     : .NET 10.0.10 (10.0.1026.32716), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  Job-ZYOFQR : .NET 10.0.10 (10.0.1026.32716), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI

InvocationCount=1  IterationCount=15  UnrollFactor=1  
WarmupCount=10  Error=NA  RatioSD=?  

```
| Method                                | Job        | Toolchain              | LaunchCount | Mean | Min | Max | Ratio | Alloc Ratio |
|-------------------------------------- |----------- |----------------------- |------------ |-----:|----:|----:|------:|------------:|
| MassTransit_InMemory_Publish          | Job-ZYOFQR | Default                | Default     |   NA |  NA |  NA |     ? |           ? |
| Wolverine_InMemory_Publish            | Job-ZYOFQR | Default                | Default     |   NA |  NA |  NA |     ? |           ? |
| EricksonLopezOutbox_StoreAsync_Single | Job-ZYOFQR | Default                | Default     |   NA |  NA |  NA |     ? |           ? |
| EricksonLopezOutbox_StoreAsync_Fluent | Job-ZYOFQR | Default                | Default     |   NA |  NA |  NA |     ? |           ? |
|                                       |            |                        |             |      |     |     |       |             |
| MassTransit_InMemory_Publish          | MediumRun  | InProcessEmitToolchain | 2           |   NA |  NA |  NA |     ? |           ? |
| Wolverine_InMemory_Publish            | MediumRun  | InProcessEmitToolchain | 2           |   NA |  NA |  NA |     ? |           ? |
| EricksonLopezOutbox_StoreAsync_Single | MediumRun  | InProcessEmitToolchain | 2           |   NA |  NA |  NA |     ? |           ? |
| EricksonLopezOutbox_StoreAsync_Fluent | MediumRun  | InProcessEmitToolchain | 2           |   NA |  NA |  NA |     ? |           ? |

Benchmarks with issues:
  C_StoreBenchmarks.MassTransit_InMemory_Publish: Job-ZYOFQR(InvocationCount=1, IterationCount=15, UnrollFactor=1, WarmupCount=10)
  C_StoreBenchmarks.Wolverine_InMemory_Publish: Job-ZYOFQR(InvocationCount=1, IterationCount=15, UnrollFactor=1, WarmupCount=10)
  C_StoreBenchmarks.EricksonLopezOutbox_StoreAsync_Single: Job-ZYOFQR(InvocationCount=1, IterationCount=15, UnrollFactor=1, WarmupCount=10)
  C_StoreBenchmarks.EricksonLopezOutbox_StoreAsync_Fluent: Job-ZYOFQR(InvocationCount=1, IterationCount=15, UnrollFactor=1, WarmupCount=10)
  C_StoreBenchmarks.MassTransit_InMemory_Publish: MediumRun(Toolchain=InProcessEmitToolchain, InvocationCount=1, IterationCount=15, LaunchCount=2, UnrollFactor=1, WarmupCount=10)
  C_StoreBenchmarks.Wolverine_InMemory_Publish: MediumRun(Toolchain=InProcessEmitToolchain, InvocationCount=1, IterationCount=15, LaunchCount=2, UnrollFactor=1, WarmupCount=10)
  C_StoreBenchmarks.EricksonLopezOutbox_StoreAsync_Single: MediumRun(Toolchain=InProcessEmitToolchain, InvocationCount=1, IterationCount=15, LaunchCount=2, UnrollFactor=1, WarmupCount=10)
  C_StoreBenchmarks.EricksonLopezOutbox_StoreAsync_Fluent: MediumRun(Toolchain=InProcessEmitToolchain, InvocationCount=1, IterationCount=15, LaunchCount=2, UnrollFactor=1, WarmupCount=10)
