using Lumyte.Benchmarks;
using Lumyte.Worlds.Benchmark;

return BenchmarkEntry.Run(args, typeof(WorldsBenchmarks).Assembly, BenchmarkChecks.Verify);
