using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Benchmarks.Testing;

internal sealed class CpuTable(CpuGraphicsDevice owner, ArgumentTableDesc desc) : CpuResource(owner), IArgumentTable
{
    private readonly Dictionary<(int Kind, uint Slot), (IShaderReference Reference, ulong Generation)> _entries = [];

    private ulong _generation;

    public uint TextureCapacity => desc.TextureCapacity;

    public uint SamplerCapacity => desc.SamplerCapacity;

    public uint BufferCapacity => desc.BufferCapacity;

    public IGpuRef<IGraphicsTextureView> WriteTexture(uint slot, IGraphicsTextureView view) => Write<IGraphicsTextureView>(0, slot, TextureCapacity, view, 1, 0, 0, ShaderBufferAccess.ReadOnly);

    public IGpuRef<IGraphicsSampler> WriteSampler(uint slot, IGraphicsSampler sampler) => Write<IGraphicsSampler>(1, slot, SamplerCapacity, sampler, 1, 0, 0, ShaderBufferAccess.ReadOnly);

    public IGpuRef<T> WriteBuffer<T>(uint slot, BufferSlice<T> range, ShaderBufferAccess access = ShaderBufferAccess.ReadOnly)

        where T : unmanaged
    {
        Owner.Own(range.Buffer);
        IGraphicsBuffer<T> b = range.Buffer;
        b.ValidateByteRange(range.OffsetInBytes, range.SizeInBytes);
        if (b.Memory != MemoryPreference.Automatic || (b.Usage & BufferUsage.ShaderRead) == 0 || (access == ShaderBufferAccess.ReadWrite && (b.Usage & BufferUsage.ShaderWrite) == 0))
        {
            throw new ArgumentException("Invalid shader storage usage.");
        }

        return Write<T>(2, slot, BufferCapacity, b, range.Count, range.OffsetInBytes, range.SizeInBytes, access);
    }

    public IGpuRef<T> WriteBuffer<T>(uint slot, ShaderDataSlice<T> range)

        where T : struct, IShaderArguments
    {
        Owner.Own(range.Buffer);
        if (range.Buffer.Memory != MemoryPreference.Automatic || range.Count == 0)
        {
            throw new ArgumentException("Requires shader GPU storage.");
        }

        return Write<T>(2, slot, BufferCapacity, range.Buffer, range.Count, range.Offset * range.Buffer.ShaderElementStrideInBytes, range.Count * range.Buffer.ShaderElementStrideInBytes, ShaderBufferAccess.ReadOnly);
    }

    public void ReleaseTexture(uint slot) => Release(0, slot, TextureCapacity);

    public void ReleaseSampler(uint slot) => Release(1, slot, SamplerCapacity);

    public void ReleaseBuffer(uint slot) => Release(2, slot, BufferCapacity);

    public override void Dispose()
    {
        _entries.Clear();
        base.Dispose();
    }

    internal void Validate(IShaderReference reference, int kind, ulong revision)
    {
        Check();
        Owner.Own(reference.Resource);
        if (!_entries.TryGetValue((kind, reference.Slot), out (IShaderReference Reference, ulong Generation) current) || current.Generation != revision || current.Reference.Resource != reference.Resource)
        {
            throw new InvalidOperationException("Stale argument registration.");
        }
    }

    private IGpuRef<T> Write<T>(int kind, uint slot, uint capacity, object resource, ulong count, ulong offset, ulong size, ShaderBufferAccess access)
    {
        Check();
        Owner.Own(resource);
        if (slot >= capacity)
        {
            throw new ArgumentOutOfRangeException(nameof(slot));
        }

        ulong revision = ++_generation;
        var r = new CpuRef<T>(this, resource, kind, slot, count, offset, size, offset, size, access, revision);
        _entries[(kind, slot)] = (r, revision);
        return r;
    }

    private void Release(int kind, uint slot, uint capacity)
    {
        Check();
        if (slot >= capacity)
        {
            throw new ArgumentOutOfRangeException(nameof(slot));
        }

        _entries.Remove((kind, slot));
    }
}
