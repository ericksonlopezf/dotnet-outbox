<!-- Copyright © Erickson Lopez. MIT License. -->

```

BenchmarkDotNet v0.13.12, Windows 11 (10.0.26200.8875)
Unknown processor
.NET SDK 10.0.302
  [Host]     : .NET 10.0.10 (10.0.1026.32716), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  Job-OUHNJV : .NET 10.0.10 (10.0.1026.32716), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI


```
| Method                                     | Job        | Toolchain              | IterationCount | WarmupCount | PayloadSize | Mean        | Min         | Max         | P50         | P95         | Op/s         | Ratio | Gen0    | Gen1    | Gen2    | Allocated | Alloc Ratio |
|------------------------------------------- |----------- |----------------------- |--------------- |------------ |------------ |------------:|------------:|------------:|------------:|------------:|-------------:|------:|--------:|--------:|--------:|----------:|------------:|
| **EricksonLopezOutbox_Serialize_Allocating**   | **Job-OUHNJV** | **Default**                | **100**            | **20**          | **512**         |    **78.92 ns** |    **73.38 ns** |    **83.15 ns** |    **78.62 ns** |    **82.68 ns** | **12,670,438.9** |  **1.00** |  **0.0117** |       **-** |       **-** |     **592 B** |        **1.00** |
| EricksonLopezOutbox_Serialize_BufferWriter | Job-OUHNJV | Default                | 100            | 20          | 512         |    53.83 ns |    52.76 ns |    54.51 ns |    53.91 ns |    54.40 ns | 18,576,315.0 |  0.68 |  0.0006 |       - |       - |      32 B |        0.05 |
|                                            |            |                        |                |             |             |             |             |             |             |             |              |       |         |         |         |           |             |
| EricksonLopezOutbox_Serialize_Allocating   | Job-FWMHQE | InProcessEmitToolchain | Default        | Default     | 512         |    89.43 ns |    87.81 ns |    91.82 ns |    88.97 ns |    91.61 ns | 11,182,124.8 |  1.00 |  0.0117 |       - |       - |     592 B |        1.00 |
| EricksonLopezOutbox_Serialize_BufferWriter | Job-FWMHQE | InProcessEmitToolchain | Default        | Default     | 512         |    65.84 ns |    65.60 ns |    66.59 ns |    65.72 ns |    66.33 ns | 15,188,834.5 |  0.74 |  0.0006 |       - |       - |      32 B |        0.05 |
|                                            |            |                        |                |             |             |             |             |             |             |             |              |       |         |         |         |           |             |
| **EricksonLopezOutbox_Serialize_Allocating**   | **Job-OUHNJV** | **Default**                | **100**            | **20**          | **10240**       |   **576.70 ns** |   **520.19 ns** |   **662.82 ns** |   **572.39 ns** |   **626.21 ns** |  **1,734,003.0** |  **1.00** |  **0.2050** |  **0.0076** |       **-** |   **10320 B** |       **1.000** |
| EricksonLopezOutbox_Serialize_BufferWriter | Job-OUHNJV | Default                | 100            | 20          | 10240       |   323.54 ns |   319.98 ns |   326.75 ns |   323.56 ns |   325.91 ns |  3,090,834.2 |  0.56 |  0.0005 |       - |       - |      32 B |       0.003 |
|                                            |            |                        |                |             |             |             |             |             |             |             |              |       |         |         |         |           |             |
| EricksonLopezOutbox_Serialize_Allocating   | Job-FWMHQE | InProcessEmitToolchain | Default        | Default     | 10240       |   593.30 ns |   548.10 ns |   650.00 ns |   589.95 ns |   636.67 ns |  1,685,499.2 |  1.00 |  0.2050 |  0.0076 |       - |   10320 B |       1.000 |
| EricksonLopezOutbox_Serialize_BufferWriter | Job-FWMHQE | InProcessEmitToolchain | Default        | Default     | 10240       |   336.76 ns |   332.59 ns |   338.56 ns |   337.06 ns |   338.50 ns |  2,969,516.1 |  0.58 |  0.0005 |       - |       - |      32 B |       0.003 |
|                                            |            |                        |                |             |             |             |             |             |             |             |              |       |         |         |         |           |             |
| **EricksonLopezOutbox_Serialize_Allocating**   | **Job-OUHNJV** | **Default**                | **100**            | **20**          | **102400**      | **6,013.08 ns** | **5,839.78 ns** | **6,306.65 ns** | **6,009.60 ns** | **6,188.06 ns** |    **166,304.2** |  **1.00** | **32.2571** | **32.2571** | **32.2571** |  **102513 B** |       **1.000** |
| EricksonLopezOutbox_Serialize_BufferWriter | Job-OUHNJV | Default                | 100            | 20          | 102400      | 3,419.04 ns | 3,387.15 ns | 3,467.35 ns | 3,418.24 ns | 3,447.49 ns |    292,479.8 |  0.57 |       - |       - |       - |      32 B |       0.000 |
|                                            |            |                        |                |             |             |             |             |             |             |             |              |       |         |         |         |           |             |
| EricksonLopezOutbox_Serialize_Allocating   | Job-FWMHQE | InProcessEmitToolchain | Default        | Default     | 102400      | 7,766.59 ns | 7,710.67 ns | 7,803.06 ns | 7,771.87 ns | 7,800.02 ns |    128,756.6 |  1.00 | 17.7460 | 17.7460 | 17.7460 |  102573 B |       1.000 |
| EricksonLopezOutbox_Serialize_BufferWriter | Job-FWMHQE | InProcessEmitToolchain | Default        | Default     | 102400      | 3,379.60 ns | 3,367.87 ns | 3,393.94 ns | 3,378.79 ns | 3,392.31 ns |    295,892.9 |  0.44 |       - |       - |       - |      32 B |       0.000 |
