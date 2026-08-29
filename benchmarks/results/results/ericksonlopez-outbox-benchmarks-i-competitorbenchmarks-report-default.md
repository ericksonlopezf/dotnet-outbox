<!-- Copyright © Erickson Lopez. MIT License. -->


BenchmarkDotNet v0.13.12, Windows 11 (10.0.26200.8875)
Unknown processor
.NET SDK 10.0.302
  [Host]     : .NET 10.0.10 (10.0.1026.32716), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  DefaultJob : .NET 10.0.10 (10.0.1026.32716), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


 Method                         | Mean        | Error     | StdDev    | Min         | Max         | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
------------------------------- |------------:|----------:|----------:|------------:|------------:|------:|--------:|-------:|----------:|------------:|
 CAP_StoreAsync                 |    855.7 ns |   7.30 ns |   6.47 ns |    841.0 ns |    867.8 ns |  3.34 |    0.03 | 0.0305 |    1664 B |        3.71 |
 NServiceBus_StoreAsync         | 25,423.8 ns | 194.01 ns | 181.48 ns | 25,021.9 ns | 25,754.8 ns | 99.19 |    0.88 | 0.0610 |    5457 B |       12.18 |
 EricksonLopezOutbox_StoreAsync |    256.3 ns |   1.43 ns |   1.27 ns |    254.3 ns |    258.6 ns |  1.00 |    0.00 | 0.0086 |     448 B |        1.00 |
