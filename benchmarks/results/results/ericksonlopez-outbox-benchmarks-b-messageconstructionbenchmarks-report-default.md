<!-- Copyright © Erickson Lopez. MIT License. -->


BenchmarkDotNet v0.13.12, Windows 11 (10.0.26200.8875)
Unknown processor
.NET SDK 10.0.302
  [Host]     : .NET 10.0.10 (10.0.1026.32716), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  Job-HIGIWD : .NET 10.0.10 (10.0.1026.32716), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI

IterationCount=200  WarmupCount=20  

 Method                                    | Mean      | Min       | Max       | Ratio | Gen0   | Allocated | Alloc Ratio |
------------------------------------------ |----------:|----------:|----------:|------:|-------:|----------:|------------:|
 EricksonLopezOutbox_CreateOutboxMessage   | 68.443 ns | 66.834 ns | 70.475 ns |  1.00 | 0.0041 |     208 B |        1.00 |
 EricksonLopezOutbox_CreateMessageMetadata |  2.119 ns |  1.948 ns |  2.260 ns |  0.03 | 0.0011 |      56 B |        0.27 |
