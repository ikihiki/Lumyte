using Ahjo.Wgpu.Native;
using A = Ahjo.Wgpu;

namespace Lumyte.Graphics.Wgpu;

internal class WgpuBuffer : GpuResource, IBufferBackendContract
{
    private readonly List<MaterialRegion> _materials = [];

    internal WgpuBuffer(WgpuDevice owner, A.Buffer native, ulong size, BufferUsage usage, MemoryPreference memory)
        : base(owner)
    {
        (Native, SizeInBytes, Usage, Memory) = (native, size, usage, memory);
    }

    public ulong SizeInBytes { get; }

    public BufferUsage Usage { get; }

    public MemoryPreference Memory { get; }

    internal A.Buffer Native { get; }

    public BufferSlice Slice(ulong offset, ulong length)
    {
        lock (Owner.Gate)
        {
            Check(Owner);
            if (length == 0 || offset > SizeInBytes || length > SizeInBytes - offset)
            {
                throw new ArgumentOutOfRangeException(nameof(length));
            }

            return new(this, offset, length);
        }
    }

    public void ValidateRange(ulong offset, ulong length) => Slice(offset, length);

    public void CopyFrom(ReadOnlySpan<byte> source, ulong offset, ulong length)
    {
        lock (Owner.Gate)
        {
            ValidateRange(offset, length);
            RequireIdle();
            if (Memory != MemoryPreference.Upload || SizeInBytes > int.MaxValue || (ulong)source.Length > length)
            {
                throw new ArgumentException("CPU copy requires an idle Upload buffer and a valid managed-size range.");
            }

            WGPUMapAsyncStatus status = Native.MapBlocking(Owner.Instance, A.MapMode.Write, 0, (nuint)SizeInBytes);
            if (status != WGPUMapAsyncStatus.Success)
            {
                throw new InvalidOperationException($"WgpuBuffer map failed: {status}");
            }

            try
            {
                Owner.CheckErrors();
                InvalidateMaterials(offset, (ulong)source.Length);
                source.CopyTo(Native.GetMappedRange<byte>(0, (nuint)SizeInBytes).Slice(checked((int)offset), source.Length));
            }
            finally
            {
                Native.Unmap();
            }
        }
    }

    public void CopyTo(Span<byte> destination, ulong offset, ulong length)
    {
        lock (Owner.Gate)
        {
            ValidateRange(offset, length);
            RequireIdle();
            if (Memory != MemoryPreference.Readback || SizeInBytes > int.MaxValue || length > (ulong)destination.Length)
            {
                throw new ArgumentException("CPU copy requires completed Readback memory and a sufficient destination.");
            }

            WGPUMapAsyncStatus status = Native.MapBlocking(Owner.Instance, A.MapMode.Read, 0, (nuint)SizeInBytes);
            if (status != WGPUMapAsyncStatus.Success)
            {
                throw new InvalidOperationException($"WgpuBuffer map failed: {status}");
            }

            try
            {
                Owner.CheckErrors();
                Native.GetConstMappedRange<byte>(0, (nuint)SizeInBytes).Slice(checked((int)offset), checked((int)length)).CopyTo(destination);
            }
            finally
            {
                Native.Unmap();
            }
        }
    }

    internal MaterialRegion? FindMaterial(ulong offset, ulong length) => _materials.Find(r => r.Valid && r.Ready && r.Offset == offset && r.Length == length);

    internal void InvalidateMaterials(ulong offset, ulong length)
    {
        foreach (MaterialRegion? region in _materials.Where(r => r.Offset < offset + length && offset < r.Offset + r.Length).ToArray())
        {
            region.Valid = false;
            region.Bindings.ReleaseLease();
            _materials.Remove(region);
        }
    }

    internal MaterialRegion RegisterMaterial(ulong offset, ulong length, MaterialBindings bindings, bool ready = true)
    {
        InvalidateMaterials(offset, length);
        bindings.Acquire();
        var region = new MaterialRegion(this, offset, length, bindings)
        {
            Ready = ready,
        };
        _materials.Add(region);
        return region;
    }

    internal void CancelMaterial(MaterialRegion region)
    {
        if (_materials.Remove(region))
        {
            region.Valid = false;
            region.Bindings.ReleaseLease();
        }
    }

    protected override void ReleaseNative()
    {
        InvalidateMaterials(0, SizeInBytes);
        Native.Dispose();
    }
}
