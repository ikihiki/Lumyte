using System.Text.Json;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using Lumyte.Diagnostics.Benchmarks;
using Perfolizer.Horology;

if (args.Contains("--verify", StringComparer.Ordinal))
{
    foreach (Workload workload in Enum.GetValues<Workload>())
    {
        new SerializationBenchmarks { Workload = workload }.Setup();
        WireBatch batch = Fixtures.Create(workload);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            workload = workload.ToString(),
            json = Serializers.JsonEncode(batch).Length,
            messagepack = Serializers.MessagePackEncode(batch).Length,
            memorypack = Serializers.MemoryPackEncode(batch).Length,
            protobuf = Serializers.ProtobufEncode(batch).Length,
        }));
    }
}
else
{
    Job job = Job.ShortRun.WithLaunchCount(1).WithWarmupCount(3).WithIterationCount(5)
        .WithIterationTime(TimeInterval.FromMilliseconds(150));
    BenchmarkSwitcher.FromAssembly(typeof(SerializationBenchmarks).Assembly).Run(args, DefaultConfig.Instance.AddJob(job));
}
