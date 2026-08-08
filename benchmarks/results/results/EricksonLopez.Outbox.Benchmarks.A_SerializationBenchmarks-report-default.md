
BenchmarkDotNet v0.13.12, Windows 11 (10.0.26200.8875)
Unknown processor
.NET SDK 10.0.302
  [Host]     : .NET 10.0.10 (10.0.1026.32716), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  Job-WHNGNK : .NET 10.0.10 (10.0.1026.32716), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI

IterationCount=100  WarmupCount=20  

 Method                                     | Mean     | Min      | Max      | Ratio | Gen0   | Allocated | Alloc Ratio |
------------------------------------------- |---------:|---------:|---------:|------:|-------:|----------:|------------:|
 EricksonLopezOutbox_Serialize_Allocating   | 75.67 ns | 74.18 ns | 77.58 ns |  1.00 | 0.0029 |     144 B |        1.00 |
 EricksonLopezOutbox_Serialize_BufferWriter | 70.91 ns | 69.74 ns | 72.01 ns |  0.94 | 0.0006 |      32 B |        0.22 |
