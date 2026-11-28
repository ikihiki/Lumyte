using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Planning.Tests;

/// <summary>Provides deterministic managed test instrumentation.</summary>
internal sealed class ManagedTable(ArgumentTableDesc desc, List<string> events) : IArgumentTable
{
    public uint TextureCapacity => desc.TextureCapacity;

    public uint SamplerCapacity => desc.SamplerCapacity;

    public uint BufferCapacity => desc.BufferCapacity;

    public IGpuRef<T> WriteBuffer<T>(uint slot, BufferSlice<T> range, ShaderBufferAccess access = ShaderBufferAccess.ReadOnly)
        where T : unmanaged
    {
        events.Add("register-buffer");
        return new ManagedReference<T>(range.Count, this, range.Buffer, slot, range.OffsetInBytes, range.Buffer.Layout.ElementStrideInBytes, access);
    }

    public IGpuRef<IGraphicsTextureView> WriteTexture(uint slot, IGraphicsTextureView view) => new ManagedReference<IGraphicsTextureView>(1, this, view, slot, stride: 0);

    public IGpuRef<IGraphicsSampler> WriteSampler(uint slot, IGraphicsSampler sampler) => throw new NotSupportedException();

    public IGpuRef<T> WriteBuffer<T>(uint slot, ShaderDataSlice<T> range)
        where T : struct, IShaderArguments
    {
        events.Add("register-shaderdata");
        return new ManagedReference<T>(range.Count, this, range.Buffer, slot, checked(range.Offset * range.Buffer.ShaderElementStrideInBytes), range.Buffer.ShaderElementStrideInBytes);
    }

    public void ReleaseTexture(uint slot)
    {
    }

    public void ReleaseSampler(uint slot)
    {
    }

    public void ReleaseBuffer(uint slot)
    {
    }

    public void Dispose() => events.Add("dispose-table");
}
