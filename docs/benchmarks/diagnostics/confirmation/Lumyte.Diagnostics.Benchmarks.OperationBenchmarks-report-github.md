# OperationBenchmarks — BenchmarkDotNet 出力

```text

BenchmarkDotNet v0.15.8, Linux Debian GNU/Linux 13 (trixie)
AMD EPYC 9V74 2.60GHz, 1 CPU, 5 logical and 5 physical cores
.NET SDK 10.0.401
  [Host]   : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4
  ShortRun : .NET 10.0.12 (10.0.12, 10.0.1226.42308), X64 RyuJIT x86-64-v4

Job=ShortRun  IterationCount=5  IterationTime=150ms
LaunchCount=1  WarmupCount=3

```

| Method     | Mean       | Error      | StdDev     | Ratio   | RatioSD   | Gen0     | Allocated   | Alloc Ratio   |
| ---------- | ---------: | ---------: | ---------: | ------: | --------: | -------: | ----------: | ------------: |
| Direct     | 10.41 ns   | 2.296 ns   | 0.596 ns   | 1.00    | 0.07      | 0.0043   | 72 B        | 1.00          |
| Generated  | 79.88 ns   | 4.590 ns   | 1.192 ns   | 7.70    | 0.41      | 0.0237   | 400 B       | 5.56          |
