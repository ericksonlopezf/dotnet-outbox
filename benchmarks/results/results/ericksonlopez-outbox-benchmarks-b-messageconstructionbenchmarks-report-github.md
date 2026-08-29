<!-- Copyright © Erickson Lopez. MIT License. -->

```

BenchmarkDotNet v0.13.12, Windows 11 (10.0.26200.8875)
Unknown processor
.NET SDK 10.0.302
  [Host]     : .NET 10.0.10 (10.0.1026.32716), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  Job-BVYALF : .NET 10.0.10 (10.0.1026.32716), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                                    | Job        | Toolchain              | IterationCount | WarmupCount | Mean      | Min       | Max       | P50       | P95       | Op/s          | Ratio | Gen0   | Allocated | Alloc Ratio |
|------------------------------------------ |----------- |----------------------- |--------------- |------------ |----------:|----------:|----------:|----------:|----------:|--------------:|------:|-------:|----------:|------------:|
| EricksonLopezOutbox_CreateOutboxMessage   | Job-BVYALF | Default                | 200            | 20          | 68.097 ns | 66.851 ns | 69.199 ns | 68.057 ns | 68.983 ns |  14,684,910.8 |  1.00 | 0.0041 |     208 B |        1.00 |
| EricksonLopezOutbox_CreateMessageMetadata | Job-BVYALF | Default                | 200            | 20          |  2.062 ns |  1.993 ns |  2.200 ns |  2.052 ns |  2.150 ns | 484,942,929.6 |  0.03 | 0.0011 |      56 B |        0.27 |
|                                           |            |                        |                |             |           |           |           |           |           |               |       |        |           |             |
| EricksonLopezOutbox_CreateOutboxMessage   | Job-FWMHQE | InProcessEmitToolchain | Default        | Default     | 78.069 ns | 76.537 ns | 79.067 ns | 78.307 ns | 79.006 ns |  12,809,150.2 |  1.00 | 0.0041 |     208 B |        1.00 |
| EricksonLopezOutbox_CreateMessageMetadata | Job-FWMHQE | InProcessEmitToolchain | Default        | Default     |  2.390 ns |  2.343 ns |  2.498 ns |  2.375 ns |  2.477 ns | 418,370,261.0 |  0.03 | 0.0011 |      56 B |        0.27 |
