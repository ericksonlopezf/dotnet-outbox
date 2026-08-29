<!-- Copyright © Erickson Lopez. MIT License. -->

```

BenchmarkDotNet v0.13.12, Windows 11 (10.0.26200.8875)
Unknown processor
.NET SDK 10.0.302
  [Host]     : .NET 10.0.10 (10.0.1026.32716), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  Job-HIGIWD : .NET 10.0.10 (10.0.1026.32716), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI

IterationCount=200  WarmupCount=20  

```
| Method            | Mean     | Min      | Max      | Ratio | Allocated | Alloc Ratio |
|------------------ |---------:|---------:|---------:|------:|----------:|------------:|
| InMemory_GetAlias | 1.369 ns | 1.343 ns | 1.394 ns |  1.00 |         - |          NA |
| InMemory_Resolve  | 2.594 ns | 2.555 ns | 2.651 ns |  1.89 |         - |          NA |
