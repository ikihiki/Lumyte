namespace Lumyte.Graphics.RenderGraph;

/// <summary>Low-allocation render-graph callback with explicit state.</summary>
public delegate void GpuRenderGraphPassAction<TState>(
    GpuRenderGraphPassContextView context,
    TState state);
