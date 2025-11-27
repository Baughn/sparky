```

BenchmarkDotNet v0.15.6, Linux NixOS 25.11 (Xantusia)
AMD Ryzen 9 7950X3D 0.42GHz, 1 CPU, 32 logical and 16 physical cores
.NET SDK 8.0.416
  [Host]     : .NET 8.0.22 (8.0.22, 8.0.2225.52707), X64 RyuJIT x86-64-v4
  DefaultJob : .NET 8.0.22 (8.0.22, 8.0.2225.52707), X64 RyuJIT x86-64-v4
  Job-CNUJVU : .NET 8.0.22 (8.0.22, 8.0.2225.52707), X64 RyuJIT x86-64-v4


```
| Method                                 | Job        | InvocationCount | UnrollFactor | Mean         | Error      | StdDev       | Median       | Gen0   | Allocated  |
|--------------------------------------- |----------- |---------------- |------------- |-------------:|-----------:|-------------:|-------------:|-------:|-----------:|
| &#39;Linear DC solve: 200-resistor ladder&#39; | DefaultJob | Default         | 16           | 35,720.80 μs |  97.256 μs |    86.215 μs | 35,684.33 μs |      - |  167.02 KB |
| &#39;Non-linear DC: diode clipper&#39;         | DefaultJob | Default         | 16           |     15.71 μs |   0.231 μs |     0.204 μs |     15.65 μs | 0.5188 |   26.64 KB |
| &#39;Transient RC: 200 steps @ 10us&#39;       | Job-CNUJVU | 1               | 1            | 10,089.00 μs | 783.755 μs | 2,310.919 μs |  9,042.66 μs |      - | 5514.02 KB |
