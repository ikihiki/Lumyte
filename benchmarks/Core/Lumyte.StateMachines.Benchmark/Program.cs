using Lumyte.Benchmarks;
using Lumyte.StateMachines.Benchmark;

return BenchmarkEntry.Run(args, typeof(StateMachinesBenchmarks).Assembly, BenchmarkChecks.Verify);
