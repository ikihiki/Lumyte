using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation.Tests;

internal sealed class TypedTable(ArgumentTableDesc desc) : IArgumentTable
{
    public uint TextureCapacity => desc.TextureCapacity;

    public uint SamplerCapacity => desc.SamplerCapacity;

    public uint BufferCapacity => desc.BufferCapacity;

    internal object? LastResource { get; private set; }

    internal Type? LastElementType { get; private set; }

    internal object? LastReference { get; private set; }

    internal bool FailRegistration { get; set; }

    public IGpuRef<IGraphicsTextureView> WriteTexture(uint slot, IGraphicsTextureView view) => throw new NotSupportedException();

    public IGpuRef<IGraphicsSampler> WriteSampler(uint slot, IGraphicsSampler sampler) => Register<IGraphicsSampler>(slot, sampler, 0, 1, 16);

    public IGpuRef<T> WriteBuffer<T>(uint slot, BufferSlice<T> range, ShaderBufferAccess access = ShaderBufferAccess.ReadOnly)
        where T : unmanaged => throw new NotSupportedException();

    public IGpuRef<T> WriteBuffer<T>(uint slot, ShaderDataSlice<T> range)
        where T : struct, IShaderArguments
        => Register<T>(slot, range.Buffer, range.Offset, range.Count, range.Buffer.ShaderElementStrideInBytes);

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

    private IGpuRef<T> Register<T>(uint slot, object resource, ulong offset, ulong count, ulong stride)
    {
        if (FailRegistration)
        {
            throw new InvalidOperationException("Injected registration failure.");
        }

        LastResource = resource;
        LastElementType = typeof(T);
        var reference = new TypedReference<T>(this, resource, slot, offset * stride, count * stride, count, stride);
        LastReference = reference;
        return reference;
    }
}
