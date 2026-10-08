using Ahjo.Wgpu.Native;
using A = Ahjo.Wgpu;

namespace Lumyte.Graphics.Wgpu;

internal class WgpuBuffer : GpuResource, IBufferBackendContract
{
    private readonly List<ShaderDataRegion> _shaderData = [];

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
                InvalidateShaderData(offset, (ulong)source.Length);
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

    internal ShaderDataRegion? FindShaderData(ulong offset, ulong length)
    {
        ShaderDataRegion? region = _shaderData.Find(r => r.Valid && r.Ready && r.Offset <= offset && offset - r.Offset <= r.Length && length <= r.Length - (offset - r.Offset));
        if (region is null)
        {
            return null;
        }

        ulong stride = region.Snapshot.Schema.ElementStrideInBytes;
        if ((offset - region.Offset) % stride != 0 || length % stride != 0)
        {
            return null;
        }

        return new(this, offset, length, region.Snapshot, checked(region.FirstElement + (int)((offset - region.Offset) / stride))) { Ready = true };
    }

    internal IEnumerable<ShaderDataRegion> FindShaderDataCopies(ulong offset, ulong length)
    {
        foreach (ShaderDataRegion region in _shaderData.Where(r => r.Valid && r.Ready && r.Offset < offset + length && offset < r.Offset + r.Length))
        {
            ulong first = Math.Max(region.Offset, offset);
            ulong last = Math.Min(region.Offset + region.Length, offset + length);
            ulong stride = region.Snapshot.Schema.ElementStrideInBytes;
            if ((first - region.Offset) % stride == 0 && (last - first) % stride == 0)
            {
                yield return new(this, first, last - first, region.Snapshot, checked(region.FirstElement + (int)((first - region.Offset) / stride))) { Ready = true };
            }
        }
    }

    internal bool IsCurrent(ShaderDataRegion reference) => _shaderData.Any(region => region.Valid && region.Ready && ReferenceEquals(region.Snapshot, reference.Snapshot) && region.Offset <= reference.Offset && reference.Offset - region.Offset <= region.Length && reference.Length <= region.Length - (reference.Offset - region.Offset) && reference.FirstElement == region.FirstElement + (int)((reference.Offset - region.Offset) / region.Snapshot.Schema.ElementStrideInBytes));

    internal void InvalidateShaderData(ulong offset, ulong length)
    {
        if (length == 0)
        {
            return;
        }

        foreach (ShaderDataRegion region in _shaderData.Where(r => r.Offset < offset + length && offset < r.Offset + r.Length).ToArray())
        {
            ulong stride = region.Snapshot.Schema.ElementStrideInBytes;
            ulong prefix = offset > region.Offset ? ((offset - region.Offset) / stride) * stride : 0;
            ulong suffix = Math.Min(region.Length, ((Math.Min(offset + length, region.Offset + region.Length) - region.Offset + stride - 1) / stride) * stride);
            if (prefix > 0)
            {
                Preserve(region, region.Offset, prefix, region.FirstElement);
            }

            if (suffix < region.Length)
            {
                Preserve(region, region.Offset + suffix, region.Length - suffix, checked(region.FirstElement + (int)(suffix / stride)));
            }

            region.Valid = false;
            region.Snapshot.ReleaseLease();
            _shaderData.Remove(region);
        }
    }

    internal ShaderDataRegion RegisterShaderData(ulong offset, ulong length, ShaderDataSnapshot snapshot, int firstElement = 0, bool ready = true)
    {
        InvalidateShaderData(offset, length);
        snapshot.Acquire();
        var region = new ShaderDataRegion(this, offset, length, snapshot, firstElement)
        {
            Ready = ready,
        };
        _shaderData.Add(region);
        return region;
    }

    internal void CancelShaderData(ShaderDataRegion region)
    {
        if (_shaderData.Remove(region))
        {
            region.Valid = false;
            region.Snapshot.ReleaseLease();
        }
    }

    protected override void ReleaseNative()
    {
        InvalidateShaderData(0, SizeInBytes);
        Native.Dispose();
    }

    private void Preserve(ShaderDataRegion region, ulong offset, ulong length, int firstElement)
    {
        region.Snapshot.Acquire();
        _shaderData.Add(new(this, offset, length, region.Snapshot, firstElement) { Ready = region.Ready });
    }
}
