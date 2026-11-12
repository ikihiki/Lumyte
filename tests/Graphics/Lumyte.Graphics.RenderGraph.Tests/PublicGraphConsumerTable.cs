using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.RenderGraph.Tests;

internal sealed class PublicGraphConsumerTable(ArgumentTableDesc desc) : IArgumentTable
{
    public uint TextureCapacity => desc.TextureCapacity;

    public uint SamplerCapacity => desc.SamplerCapacity;

    public uint BufferCapacity => desc.BufferCapacity;

    public IGpuRef<IGraphicsTextureView> WriteTexture(uint slot, IGraphicsTextureView view) => new PublicGraphConsumerRef<IGraphicsTextureView>(1);

    public IGpuRef<IGraphicsSampler> WriteSampler(uint slot, IGraphicsSampler sampler) => new PublicGraphConsumerRef<IGraphicsSampler>(1);

    public IGpuRef<T> WriteBuffer<T>(uint slot, BufferSlice<T> range)
        where T : unmanaged => new PublicGraphConsumerRef<T>(range.Count);

    public IGpuRef<T> WriteBuffer<T>(uint slot, ShaderDataSlice<T> range)
        where T : struct, IShaderData => new PublicGraphConsumerRef<T>(range.Count);

    public void ReleaseTexture(uint slot)
    {
    }

    public void ReleaseSampler(uint slot)
    {
    }

    public void ReleaseBuffer(uint slot)
    {
    }

    public void Dispose()
    {
    }
}
