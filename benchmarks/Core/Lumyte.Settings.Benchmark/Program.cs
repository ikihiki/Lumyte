using Lumyte.Benchmarks;
using Lumyte.Settings.Benchmark;

return BenchmarkEntry.Run(args, typeof(SettingsBenchmarks).Assembly, BenchmarkChecks.Verify);
