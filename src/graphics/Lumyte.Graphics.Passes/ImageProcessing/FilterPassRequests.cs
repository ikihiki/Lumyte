using System.Numerics;

using Lumyte.Graphics.RenderGraph;

namespace Lumyte.Graphics.Passes;

public enum ImageSampling { Nearest, Linear }
public enum TargetContent { Preserve, Clear }
public sealed record BlitPassRequest(GpuRenderGraphTexture Source, GpuRenderGraphTexture Target,
    GpuGraphValue<ImageSampling> Filter)
{
    public BlitPassRequest(GpuRenderGraphTexture source, GpuRenderGraphTexture target) : this(source, target, ImageSampling.Linear) { }
}
public sealed record BlurPassRequest(GpuRenderGraphTexture Source, GpuGraphValue<int> Radius);
public readonly record struct BlurPassResult(GpuRenderGraphTexture Color);
public sealed record CompositeLayer(GpuRenderGraphTexture Source, GpuGraphValue<float> Opacity)
{
    public CompositeLayer(GpuRenderGraphTexture source) : this(source, 1f) { }
}
public sealed record CompositePassRequest(GpuRenderGraphTexture Target, IReadOnlyList<CompositeLayer> Layers,
    TargetContent Content, GpuGraphValue<Vector4> ClearColor)
{
    public CompositePassRequest(GpuRenderGraphTexture target, IReadOnlyList<CompositeLayer> layers, TargetContent content = TargetContent.Preserve)
        : this(target, layers, content, Vector4.Zero) { }
}
public sealed record ToneMapPassRequest(GpuRenderGraphTexture Source, GpuGraphValue<float> ExposureStops)
{
    public ToneMapPassRequest(GpuRenderGraphTexture source) : this(source, 0f) { }
}
public readonly record struct ToneMapPassResult(GpuRenderGraphTexture Color);
