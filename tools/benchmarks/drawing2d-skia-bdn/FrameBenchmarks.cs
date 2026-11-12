using System.Diagnostics;
using System.Diagnostics.Tracing;
using BenchmarkDotNet.Attributes;

public class FrameBenchmarks
{
    [Params("rect-static", "rect-dynamic", "path-static", "path-dynamic", "glyph-static", "glyph-dynamic")]
    public string Case { get; set; } = "rect-static";

    private IAdapter adapter = null!;
    private int nextState;
    private readonly Guid profilingRun = Guid.NewGuid();
    private EventSource? drawingProvider;
    private long diagnosticFrame;

    [GlobalSetup(Target = nameof(SkiaGpu))]
    public void SetupSkia() => Setup(skia: true);

    [GlobalSetup(Target = nameof(LumyteGpu))]
    public void SetupLumyte() => Setup(skia: false);

    private void Setup(bool skia)
    {
        var trace = new Trace(Case);
        adapter = skia ? new SkiaAdapter(trace, aa: true) : new LumyteAdapter(trace);
        // Initialize lazy shaders/pipelines/resources outside timed workloads on both sides.
        // The setup frame is fully completed and retired; only static CPU recordings survive.
        using (Activity setup = StartFrame(-1, "bdn-setup"))
        {
            adapter.Record(0);
            adapter.RenderAsync().GetAwaiter().GetResult();
        }

        drawingProvider = EventSource.GetSources().FirstOrDefault(source => source.Name == "Lumyte-Drawing2D-Profiling");
        nextState = 0;
        Console.WriteLine($"Hardware: {adapter.Identity}; case={Case}; RGBA8/sRGB/premul; 1920x1080");
    }

    [Benchmark(Baseline = true)]
    public async Task SkiaGpu()
    {
        using Activity? diagnostic = drawingProvider?.IsEnabled() == true ? StartFrame(++diagnosticFrame, "bdn-extra-frame") : null;
        adapter.Record(nextState++ & 31);
        await adapter.RenderAsync();
    }

    [Benchmark]
    public async Task LumyteGpu()
    {
        adapter.Record(nextState++ & 31);
        await adapter.RenderAsync();
    }

    private Activity StartFrame(long frameId, string name)
    {
        Activity activity = new Activity(name).Start();
        activity.SetCustomProperty("Lumyte.Profiling.RunId", profilingRun);
        activity.SetCustomProperty("Lumyte.Profiling.FrameId", frameId);
        return activity;
    }

    [GlobalCleanup]
    public void Cleanup() => adapter.Dispose();
}
