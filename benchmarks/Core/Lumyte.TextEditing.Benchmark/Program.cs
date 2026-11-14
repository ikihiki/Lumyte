using Lumyte.Benchmarks;
using Lumyte.TextEditing.Benchmark;

return BenchmarkEntry.Run(args, typeof(TextBenchmarks).Assembly, BenchmarkChecks.Verify);
