namespace Lumyte.Graphics.RenderGraph;

/// <summary>
/// Collects independently registered render-graph contributions and builds them in a deterministic order.
/// </summary>
public sealed class GpuRenderGraphFrameBuilder
{
    private readonly object sync = new();
    private readonly List<GpuRenderGraphContribution> contributions = [];
    private readonly HashSet<string> names = new(StringComparer.Ordinal);

    public int ContributionCount
    {
        get { lock (sync) { return contributions.Count; } }
    }

    /// <summary>Adds a contributor whose callback receives explicit state.</summary>
    public GpuRenderGraphFrameBuilder AddContributor<TState>(
        string name,
        TState state,
        Action<GpuRenderGraphContributionContext, TState> contribute,
        int order = 0,
        bool enabled = true)
    {
        ValidateNamespace(name);
        ArgumentNullException.ThrowIfNull(contribute);
        lock (sync)
        {
            if (!names.Add(name))
            {
                throw new ArgumentException(
                    $"A render-graph contributor named '{name}' is already registered.",
                    nameof(name));
            }
            contributions.Add(new GpuRenderGraphStatefulContribution<TState>(
                name,
                order,
                enabled,
                state,
                contribute));
        }
        return this;
    }

    /// <summary>Runs the registration phase and returns a graph ready to compile.</summary>
    public GpuRenderGraph BuildGraph()
    {
        GpuRenderGraphContribution[] snapshot;
        lock (sync) { snapshot = [.. contributions]; }
        Array.Sort(snapshot, static (left, right) =>
        {
            int order = left.Order.CompareTo(right.Order);
            return order != 0
                ? order
                : StringComparer.Ordinal.Compare(left.Name, right.Name);
        });

        var graph = new GpuRenderGraph();
        var sharedTextures = new Dictionary<string, GpuRenderGraphTexture>(StringComparer.Ordinal);
        var sharedBuffers = new Dictionary<string, GpuRenderGraphBuffer>(StringComparer.Ordinal);
        var sharedDependencies = new Dictionary<string, GpuRenderGraphDependency>(StringComparer.Ordinal);
        var sharedNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (GpuRenderGraphContribution contribution in snapshot)
        {
            if (!contribution.Enabled) { continue; }
            var context = new GpuRenderGraphContributionContext(
                graph,
                contribution.Name,
                sharedTextures,
                sharedBuffers,
                sharedDependencies,
                sharedNames);
            try { contribution.Invoke(context); }
            finally { context.Close(); }
        }
        return graph;
    }

    public GpuRenderGraphPlan Compile() => BuildGraph().Compile();

    public GpuRenderGraphPlan Compile(GpuRenderGraphPlanCache cache)
    {
        ArgumentNullException.ThrowIfNull(cache);
        return BuildGraph().Compile(cache);
    }

    private static void ValidateNamespace(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Contains("::", StringComparison.Ordinal))
        {
            throw new ArgumentException("Contributor names cannot contain '::'.", nameof(name));
        }
    }

}
