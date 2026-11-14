using Lumyte.Benchmarks;
using Lumyte.Memory.Benchmark;

return BenchmarkEntry.Run(args, typeof(MemoryBenchmarks).Assembly, BenchmarkChecks.Verify);
