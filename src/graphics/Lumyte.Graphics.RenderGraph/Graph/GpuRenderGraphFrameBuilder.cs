namespace Lumyte.Graphics.RenderGraph;

/// <summary>Composes feature graphs on the CPU when their structure changes. No GPU command callback is accepted.</summary>
public sealed class GpuRenderGraphFrameBuilder
{
    private readonly Dictionary<string, Contribution> contributors = new(StringComparer.Ordinal);

    public void AddContributor<TState>(string name, TState state,
        Action<GpuRenderGraphContributionContext, TState> callback, int order = 0, bool enabled = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(callback);
        if (!contributors.TryAdd(name, new(name, order, enabled, context => callback(context, state))))
        { throw new ArgumentException($"Contributor '{name}' is already registered.", nameof(name)); }
    }

    public GpuRenderGraph BuildGraph()
    {
        var graph = new GpuRenderGraph();
        var published = new Dictionary<string, GpuRenderGraphResource>(StringComparer.Ordinal);
        foreach (var contribution in contributors.Values.Where(item => item.Enabled)
                     .OrderBy(item => item.Order).ThenBy(item => item.Name, StringComparer.Ordinal).ToArray())
        {
            using var names = graph.EnterNamespace(contribution.Name);
            var context = new GpuRenderGraphContributionContext(graph, published);
            try { contribution.Build(context); }
            finally { context.Close(); }
        }
        return graph;
    }

    public GpuRenderGraphPlan Compile(GpuRenderGraphPlanCache? cache = null) => BuildGraph().Compile(cache);
    private sealed record Contribution(string Name, int Order, bool Enabled, Action<GpuRenderGraphContributionContext> Build);
}

public sealed class GpuRenderGraphContributionContext
{
    private readonly GpuRenderGraph graph;
    private readonly Dictionary<string, GpuRenderGraphResource> published;
    private bool closed;
    internal GpuRenderGraphContributionContext(GpuRenderGraph graph, Dictionary<string, GpuRenderGraphResource> published)
    { this.graph = graph; this.published = published; }
    public GpuRenderGraph Graph { get { Check(); return graph; } }
    public void PublishTexture(string name, GpuRenderGraphTexture texture) => Publish(name, texture);
    public void PublishBuffer(string name, GpuRenderGraphBuffer buffer) => Publish(name, buffer);
    public void PublishDependency(string name, GpuRenderGraphDependency dependency) => Publish(name, dependency);
    public GpuRenderGraphTexture GetTexture(string name) => Get<GpuRenderGraphTexture>(name);
    public GpuRenderGraphBuffer GetBuffer(string name) => Get<GpuRenderGraphBuffer>(name);
    public GpuRenderGraphDependency GetDependency(string name) => Get<GpuRenderGraphDependency>(name);

    private void Publish(string name, GpuRenderGraphResource resource)
    {
        Check();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(resource);
        graph.Require(resource.GraphIdentity);
        if (!published.TryAdd(name, resource))
        { throw new ArgumentException($"Resource '{name}' has already been published.", nameof(name)); }
    }
    private T Get<T>(string name) where T : GpuRenderGraphResource
    {
        Check();
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (!published.TryGetValue(name, out var resource))
        { throw new KeyNotFoundException($"No contributor has published '{name}'."); }
        return resource as T ?? throw new InvalidOperationException($"Published resource '{name}' is not a {typeof(T).Name}.");
    }
    internal void Close() => closed = true;
    private void Check() => ObjectDisposedException.ThrowIf(closed, this);
}
