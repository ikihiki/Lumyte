using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph.Tests;

internal sealed class PublicGraphConsumerTable(ArgumentTableDesc desc) : IArgumentTable
{
    public uint TextureCapacity => desc.TextureCapacity;

    public uint SamplerCapacity => desc.SamplerCapacity;

    public uint BufferCapacity => desc.BufferCapacity;

    internal bool Disposed { get; private set; }

    public IGpuRef<IGraphicsTextureView> WriteTexture(uint slot, IGraphicsTextureView view) => new PublicGraphConsumerRef<IGraphicsTextureView>(this, view, slot, 1);

    public IGpuRef<IGraphicsSampler> WriteSampler(uint slot, IGraphicsSampler sampler) => new PublicGraphConsumerRef<IGraphicsSampler>(this, sampler, slot, 1);

    public IGpuRef<T> WriteBuffer<T>(uint slot, BufferSlice<T> range, ShaderBufferAccess access = ShaderBufferAccess.ReadOnly)
        where T : unmanaged => new PublicGraphConsumerRef<T>(this, range.Buffer, slot, range.Count, range.OffsetInBytes, range.SizeInBytes, range.OffsetInBytes, range.SizeInBytes, access);

    public IGpuRef<T> WriteBuffer<T>(uint slot, ShaderDataSlice<T> range)
        where T : IShaderData => new PublicGraphConsumerRef<T>(this, range.Buffer, slot, range.Count, range.Offset * range.Buffer.ShaderElementStrideInBytes, range.Count * range.Buffer.ShaderElementStrideInBytes, range.Offset * range.Buffer.ShaderElementStrideInBytes, range.Count * range.Buffer.ShaderElementStrideInBytes);

    public void ReleaseTexture(uint slot)
    {
    }

    public void ReleaseSampler(uint slot)
    {
    }

    public void ReleaseBuffer(uint slot)
    {
    }

    public void Dispose() => Disposed = true;
}
