using BenchmarkDotNet.Attributes;
using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Benchmarks;

/// <summary>CPU planning and immutable input cost; no device or shader compiler is involved.</summary>
[MemoryDiagnoser]
public class RenderGraphBenchmarks
{
    private readonly GpuRenderGraphPlanCache cache = new(8);
    private GpuRenderGraph graph = null!;
    private GpuGraphInput<int> input = null!;
    private GpuRenderGraphBindingsBuilder bindings = null!;
    private int frame;

    [Params(10, 100)]
    public int PassCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        graph = new();
        input = graph.CreateInput("frame", IntegerInput.Instance, 0);
        for (var index = 0; index < PassCount; index++)
        { graph.AddPass($"feature{index}", Contract.Instance, input); }
        bindings = graph.Compile(cache).CreateBindings();
    }

    [Benchmark(Baseline = true)]
    public GpuRenderGraphPlan Compile() => graph.Compile();
    [Benchmark]
    public GpuRenderGraphPlan CompileWithCache() => graph.Compile(cache);
    [Benchmark]
    public GpuRenderGraphBindings ReusePlanAndUpdateInput()
    {
        bindings.Set(input, ++frame);
        return bindings.Build();
    }
    [Benchmark]
    public GpuRenderGraphBindings ReuseUnchangedInputs() => bindings.Build();

    private sealed class IntegerInput : IGpuGraphInputContract<int>
    {
        internal static IntegerInput Instance { get; } = new();
        public int Snapshot(int value) => value;
        public void Retain(GpuRenderInputRetentionContext context, int snapshot) { }
    }
    private sealed class Contract : IGpuRenderPassContract<GpuGraphValue<int>, int>
    {
        internal static Contract Instance { get; } = new();
        public string Id => "benchmark.feature";
        public int Version => 1;
        public GpuGraphValue<int> Snapshot(GpuGraphValue<int> request) => request;
        public int Declare(GpuPassDeclarationContext context, GpuGraphValue<int> request)
        { context.ReadInput(request, IntegerInput.Instance); context.Preserve(); return 0; }
    }
}
