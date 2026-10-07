using Lumyte.Graphics;
namespace Lumyte.Graphics.Wgpu;

internal sealed record RenderPassDesc
{
    public required TextureView Target { get; init; }
    public LoadOp Load { get; init; } = LoadOp.Clear;
    public StoreOp Store { get; init; } = StoreOp.Store;
    public Color4 ClearValue { get; init; }
}

internal sealed record GraphicsPipelineDesc
{
    public required ShaderModule Shader { get; init; }
    public string VertexEntry { get; init; } = "vertexMain";
    public string FragmentEntry { get; init; } = "fragmentMain";
}

internal sealed record ComputePipelineDesc
{
    public required ShaderModule Shader { get; init; }
    public string EntryPoint { get; init; } = "computeMain";
}

internal readonly struct BufferSlice
{
    public WgpuBuffer Buffer { get; }
    public ulong Offset { get; }
    public ulong Length { get; }
    internal BufferSlice(WgpuBuffer buffer, ulong offset, ulong length) => (Buffer, Offset, Length) = (buffer, offset, length);
}

/// <summary>A non-owning data reference. No native address, binding slot or serialization API is exposed.</summary>
internal readonly struct GpuReference<T> where T : unmanaged
{
    internal BufferSlice Data { get; }
    internal GpuReference(BufferSlice data) => Data = data;
    public override string ToString() => $"GpuReference<{typeof(T).Name}>";
}
