
BenchmarkDotNet v0.13.12, Windows 11 (10.0.26200.8875)
Unknown processor
.NET SDK 10.0.302
  [Host]     : .NET 10.0.10 (10.0.1026.32716), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  Job-KQMQYC : .NET 10.0.10 (10.0.1026.32716), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI

InvocationCount=1  IterationCount=15  UnrollFactor=1  
WarmupCount=10  Error=9.248 μs  StdDev=8.198 μs  
Median=22.85 μs  

 Method                            | Mean     | Min      | Max      | Allocated |
---------------------------------- |---------:|---------:|---------:|----------:|
 EricksonLopezOutbox_Synthetic_E2E | 18.21 μs | 6.300 μs | 28.10 μs |         - |
