```

BenchmarkDotNet v0.13.12, Windows 11 (10.0.26200.8875)
Unknown processor
.NET SDK 10.0.302
  [Host] : .NET 10.0.10 (10.0.1026.32716), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI

Toolchain=InProcessEmitToolchain  

```
| Method                                  | ThreadCount | Mean       | Error     | StdDev    | Median     | Min        | Max        | P50        | P95        | Op/s        | Gen0   | Allocated |
|---------------------------------------- |------------ |-----------:|----------:|----------:|-----------:|-----------:|-----------:|-----------:|-----------:|------------:|-------:|----------:|
| **EricksonLopezOutbox_StoreAsync_Parallel** | **1**           |   **846.7 ns** |  **11.42 ns** |  **10.68 ns** |   **844.8 ns** |   **829.6 ns** |   **868.5 ns** |   **844.8 ns** |   **864.4 ns** | **1,181,111.4** | **0.0143** |     **728 B** |
| **EricksonLopezOutbox_StoreAsync_Parallel** | **4**           | **1,545.6 ns** |  **24.50 ns** |  **20.46 ns** | **1,539.0 ns** | **1,514.7 ns** | **1,587.3 ns** | **1,539.0 ns** | **1,579.6 ns** |   **646,998.9** | **0.0515** |    **2600 B** |
| **EricksonLopezOutbox_StoreAsync_Parallel** | **16**          | **4,474.8 ns** | **172.85 ns** | **509.67 ns** | **4,316.9 ns** | **3,805.5 ns** | **5,765.8 ns** | **4,316.9 ns** | **5,529.0 ns** |   **223,472.4** | **0.1907** |    **9800 B** |
| **EricksonLopezOutbox_StoreAsync_Parallel** | **64**          | **9,699.6 ns** | **148.53 ns** | **131.66 ns** | **9,702.9 ns** | **9,435.9 ns** | **9,883.5 ns** | **9,702.9 ns** | **9,879.6 ns** |   **103,097.0** | **0.7782** |   **38601 B** |
