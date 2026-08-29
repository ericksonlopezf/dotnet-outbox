<!-- Copyright © Erickson Lopez. MIT License. -->


BenchmarkDotNet v0.13.12, Windows 11 (10.0.26200.8875)
Unknown processor
.NET SDK 10.0.302
  [Host]     : .NET 10.0.10 (10.0.1026.32716), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI
  Job-AUVMEM : .NET 10.0.10 (10.0.1026.32716), X64 RyuJIT AVX-512F+CD+BW+DQ+VL+VBMI

IterationCount=15  WarmupCount=10  

 Method                               | BatchSize | Mean         | Min          | Max          | Gen0   | Gen1   | Allocated |
------------------------------------- |---------- |-------------:|-------------:|-------------:|-------:|-------:|----------:|
 **EricksonLopezOutbox_StoreAsync_Batch** | **1**         |     **243.8 ns** |     **242.0 ns** |     **245.4 ns** | **0.0067** |      **-** |     **344 B** |
 **EricksonLopezOutbox_StoreAsync_Batch** | **10**        |   **1,806.0 ns** |   **1,771.2 ns** |   **1,839.8 ns** | **0.0668** |      **-** |    **3440 B** |
 **EricksonLopezOutbox_StoreAsync_Batch** | **100**       |  **17,559.6 ns** |  **17,229.1 ns** |  **17,936.8 ns** | **0.6714** | **0.0305** |   **34632 B** |
 **EricksonLopezOutbox_StoreAsync_Batch** | **1000**      | **176,386.9 ns** | **174,285.8 ns** | **179,714.1 ns** | **6.8359** | **1.4648** |  **350930 B** |
