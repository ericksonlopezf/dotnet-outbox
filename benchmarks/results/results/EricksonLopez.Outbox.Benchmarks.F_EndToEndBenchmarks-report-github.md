```

BenchmarkDotNet v0.13.12, Windows 11 (10.0.26200.8875)
Unknown processor
.NET SDK 10.0.302
  [Host]     : .NET 10.0.10 (10.0.1026.32716), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  Job-RAWJHY : .NET 10.0.10 (10.0.1026.32716), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI

InvocationCount=1  UnrollFactor=1  

```
| Method                            | Job        | Toolchain              | IterationCount | WarmupCount | Mean     | Min      | Max      | P50       | P95      | Op/s     | Allocated |
|---------------------------------- |----------- |----------------------- |--------------- |------------ |---------:|---------:|---------:|----------:|---------:|---------:|----------:|
| EricksonLopezOutbox_Synthetic_E2E | Job-RAWJHY | Default                | 15             | 10          | 14.77 μs | 3.600 μs | 31.90 μs |  9.200 μs | 28.89 μs | 67,689.5 |         - |
| EricksonLopezOutbox_Synthetic_E2E | Job-FAGJAM | InProcessEmitToolchain | Default        | Default     | 16.70 μs | 4.800 μs | 35.90 μs | 20.450 μs | 23.36 μs | 59,894.6 |    8800 B |
