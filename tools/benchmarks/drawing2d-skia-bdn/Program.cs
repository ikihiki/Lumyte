using System.Diagnostics.Tracing;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing.Parsers;
using Perfolizer.Mathematics.OutlierDetection;

if (args.Length == 2 && args[0] == "--verify-quality")
{
    await QualityVerification.RunAsync(args[1]);
    return;
}

if (args.Length == 2 && (args[0] == "--record-diagnostics" || args[0] == "--record-range-diagnostics"))
{
    var diagnostic = new RecordFrameBenchmarks { Case = args[1], Diagnostics = true, RangeSelection = args[0] == "--record-range-diagnostics" ? (16u, 5u) : null };
    await diagnostic.Setup();
    try { await diagnostic.CompletedFrameAsync(); }
    finally { diagnostic.Cleanup(); }
    return;
}

if (args.Length == 2 && (args[0] == "--record-quality-write" || args[0] == "--record-quality-compare"))
{
    await RecordQualityVerification.RunAsync(args[1], args[0] == "--record-quality-write");
    return;
}

// Constructor verified in cached 0.15.8 BenchmarkDotNet.xml, net8.0, member
// EventPipeProfiler.#ctor(EventPipeProfile,IReadOnlyCollection<EventPipeProvider>,Boolean).
// Custom module providers plus explicit runtime GC/alloc and CPU sampling providers.
EventPipeProvider[] providers =
[
    new("Lumyte-Drawing2D-Profiling", EventLevel.Informational, -1),
    new("Lumyte-RenderGraph-Profiling", EventLevel.Informational, -1),
    new("Lumyte-UI-Profiling", EventLevel.Informational, -1),
    new("Lumyte-UI-Application-Profiling", EventLevel.Informational, -1),
    // BDN deduplicates by provider name, user provider first: explicitly retain Default
    // CPU/JIT/loader keywords when adding verbose GC/alloc diagnostics.
    new("Microsoft-Windows-DotNETRuntime", EventLevel.Verbose,
        (long)(ClrTraceEventParser.Keywords.Default | ClrTraceEventParser.Keywords.GC |
            ClrTraceEventParser.Keywords.GCHandle | ClrTraceEventParser.Keywords.Exception)),
    new("Microsoft-DotNETCore-SampleProfiler", EventLevel.Informational, 0),
];
if (Environment.GetEnvironmentVariable("LUMYTE_RECORD_BENCH") == "1")
{
    var records = ManualConfig.Create(DefaultConfig.Instance)
        .AddJob(Job.Default.WithId("AdaptiveCompletedRecordFrame").WithOutlierMode(OutlierMode.DontRemove)
            .WithMinWarmupCount(6).WithMaxWarmupCount(10).WithMinIterationCount(15).WithMaxIterationCount(30))
        .AddColumn(StatisticColumn.Median, StatisticColumn.P95)
        .AddExporter(JsonExporter.Full)
        .AddDiagnoser(MemoryDiagnoser.Default);
    if (Environment.GetEnvironmentVariable("LUMYTE_RECORD_TRACE") == "1")
        records.AddDiagnoser(new EventPipeProfiler(EventPipeProfile.CpuSampling, providers, performExtraBenchmarksRun: true));
    BenchmarkSwitcher.FromTypes([typeof(RecordFrameBenchmarks)]).Run(args, records);
    return;
}

if (Environment.GetEnvironmentVariable("LUMYTE_CLIP_BENCH") == "1")
{
    var clips = ManualConfig.Create(DefaultConfig.Instance)
        .AddJob(Job.Default.WithId("BoundedCompletedClipFrame").WithOutlierMode(OutlierMode.DontRemove)
            .WithWarmupCount(3).WithIterationCount(8).WithIterationTime(Perfolizer.Horology.TimeInterval.FromMilliseconds(100)))
        .AddColumn(StatisticColumn.Median, StatisticColumn.P95)
        .AddExporter(JsonExporter.Full)
        .AddDiagnoser(MemoryDiagnoser.Default);
    BenchmarkSwitcher.FromTypes([typeof(ClipFrameBenchmarks)]).Run(args, clips);
    return;
}

var config = ManualConfig.Create(DefaultConfig.Instance)
    .AddJob(Job.Default.WithId("CompletedLogicalFrame").WithOutlierMode(OutlierMode.DontRemove))
    .AddColumn(StatisticColumn.Median, StatisticColumn.P95)
    .AddExporter(JsonExporter.Full)
    .AddDiagnoser(MemoryDiagnoser.Default,
        new EventPipeProfiler(EventPipeProfile.CpuSampling, providers, performExtraBenchmarksRun: true));
BenchmarkSwitcher.FromTypes([typeof(FrameBenchmarks)]).Run(args, config);
