using Lumyte.Benchmarks;
using Lumyte.Composition.Generators.Benchmark;

return BenchmarkEntry.Run(args, typeof(GeneratorBenchmarks).Assembly, BenchmarkChecks.Verify);
