namespace Lumyte.Graphics.Portable;

/// <summary>Immutable output and fixed raster state. Vertex data is accessed through shader bindings.</summary>
public sealed class GpuRasterPipelineDescription
{
    /// <remarks>The target span is copied. The runtime validates formats and fixed-state compatibility.</remarks>
    public GpuRasterPipelineDescription(ReadOnlySpan<GpuColorTargetDescription> colorTargets,
        GpuFormat? depthStencilFormat = null)
    {
        ColorTargets = Array.AsReadOnly(colorTargets.ToArray());
        DepthStencilFormat = depthStencilFormat;
    }

    public IReadOnlyList<GpuColorTargetDescription> ColorTargets { get; }
    public GpuFormat? DepthStencilFormat { get; }
    public GpuDepthStencilState DepthStencil { get; init; } = new();
    public GpuPrimitiveTopology Topology { get; init; } = GpuPrimitiveTopology.TriangleList;
    public GpuIndexFormat? StripIndexFormat { get; init; }
    public GpuCullMode CullMode { get; init; } = GpuCullMode.None;
    public GpuFrontFace FrontFace { get; init; } = GpuFrontFace.CounterClockwise;
    public uint SampleCount { get; init; } = 1;
    public uint SampleMask { get; init; } = uint.MaxValue;
    public bool AlphaToCoverage { get; init; }
}

public enum GpuPrimitiveTopology { TriangleList, TriangleStrip, LineList, LineStrip, PointList }
public enum GpuCullMode { None, Front, Back }
public enum GpuFrontFace { CounterClockwise, Clockwise }
