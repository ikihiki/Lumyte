using Lumyte.Benchmarks;
using Lumyte.Execution.Benchmark;

return BenchmarkEntry.Run(args, typeof(ExecutionBenchmarks).Assembly, BenchmarkChecks.Verify);
