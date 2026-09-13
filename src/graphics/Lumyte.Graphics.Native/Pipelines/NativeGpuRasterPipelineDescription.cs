namespace Lumyte.Graphics.Native;

/// <summary>Fixed raster values; depth/stencil behavior and stencil references are recorded separately.</summary>
/// <remarks>The backend consumes or copies the color target array during pipeline creation.</remarks>
public sealed record NativeGpuRasterPipelineDescription
{
    public NativeGpuColorTargetDescription[] ColorTargets { get; init; } = [];
    public GpuFormat? DepthStencilFormat { get; init; }
    public NativeGpuPrimitiveTopology? Topology { get; init; } = NativeGpuPrimitiveTopology.TriangleList;
    public NativeGpuMeshOutputTopology? MeshOutputTopology { get; init; }
    public NativeGpuCullMode CullMode { get; init; } = NativeGpuCullMode.None;
    public NativeGpuFrontFace FrontFace { get; init; } = NativeGpuFrontFace.CounterClockwise;
    public uint SampleCount { get; init; } = 1;
}
