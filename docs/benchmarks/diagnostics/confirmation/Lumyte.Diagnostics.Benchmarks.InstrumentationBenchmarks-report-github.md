# InstrumentationBenchmarks — BenchmarkDotNet 出力

```text

BenchmarkDotNet v0.15.8, Linux Debian GNU/Linux 13 (trixie)
AMD EPYC 9V74 2.60GHz, 1 CPU, 5 logical and 5 physical cores
.NET SDK 10.0.401
  [Host]   : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4
  ShortRun : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Job=ShortRun  IterationCount=5  IterationTime=150ms
LaunchCount=1  WarmupCount=3

```

| Method             | Enabled   | Mean           | Error         | StdDev        | Gen0       | Allocated   |
| ------------------ | --------- | -------------: | ------------: | ------------: | ---------: | ----------: |
| **RecordAndDrain** | **False** | **8.710 ns**   | **0.3883 ns** | **0.1008 ns** | **-**      | **-**       |
| **RecordAndDrain** | **True**  | **151.723 ns** | **6.8467 ns** | **1.7781 ns** | **0.0295** | **496 B**   |
