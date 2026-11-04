using BenchmarkDotNet.Attributes;
using Lumyte.Graphics.Vulkan;
using Graph = Lumyte.Graphics.RenderGraph.RenderGraph;

namespace Lumyte.Graphics.RenderGraph.Benchmarks;

/// <summary>G04 CPU declaration and compilation; never records commands or submits GPU work.</summary>
public class GraphBenchmarks
{
    private VulkanDevice _device = null!;
    private GraphFixture _fixture = null!;
    private Graph _graph = null!;

    /// <summary>Gets the bounded one-factor profile identifiers.</summary>
    public static IEnumerable<string> Profiles => GraphProfile.All.Select(profile => profile.Id);

    /// <summary>Gets or sets the graph input profile.</summary>
    [ParamsSource(nameof(Profiles))]
    public string Profile { get; set; } = "chain-p32-r32-u0";

    /// <summary>Creates the device and predeclared compilation input outside measurements.</summary>
    [GlobalSetup]
    public void Setup()
    {
        _device = VulkanDevice.Create();
        try
        {
            _fixture = new GraphFixture(_device, GraphProfile.Find(Profile));
            _graph = _fixture.Build();
            GraphChecks.Verify(_graph, GraphProfile.Find(Profile));
        }
        catch
        {
            _graph?.Dispose();
            _device.Dispose();
            throw;
        }
    }

    /// <summary>Declares a fresh real graph and disposes its builder, without compiling.</summary>
    [Benchmark]
    public void GraphBuildAndDispose()
    {
        using Graph graph = _fixture.Build();
    }

    /// <summary>Compiles an unchanged graph into a fresh plan and disposes that unrecorded plan.</summary>
    /// <returns>The real compiler statistics, consumed by BenchmarkDotNet.</returns>
    [Benchmark]
    public RenderGraphStatistics GraphCompileAndDispose()
    {
        using CompiledRenderGraph plan = _graph.Compile();
        return plan.Statistics;
    }

    /// <summary>Checks the retained graph again, then disposes the builder before the device.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        try
        {
            GraphChecks.Verify(_graph, GraphProfile.Find(Profile));
        }
        finally
        {
            _graph.Dispose();
            _device.Dispose();
        }
    }
}
