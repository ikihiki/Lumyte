namespace Lumyte.Graphics.Native;

/// <summary>A non-owning byte range relative to one linear region, never to the backing heap.</summary>
public readonly struct NativeGpuRange
{
    public NativeGpuRange(NativeGpuLinearRegion region, ulong offset, ulong size)
    {
        ArgumentNullException.ThrowIfNull(region);
        if (offset > region.Size) { throw new ArgumentOutOfRangeException(nameof(offset)); }
        if (size > region.Size - offset) { throw new ArgumentOutOfRangeException(nameof(size)); }
        Region = region;
        Offset = offset;
        Size = size;
    }

    public NativeGpuLinearRegion Region { get; }
    public ulong Offset { get; }
    public ulong Size { get; }
    public ulong GpuAddress => checked(RequireRegion().GpuAddress + Offset);

    public NativeGpuRange Slice(ulong offset, ulong size)
    {
        NativeGpuLinearRegion region = RequireRegion();
        if (offset > Size) { throw new ArgumentOutOfRangeException(nameof(offset)); }
        if (size > Size - offset) { throw new ArgumentOutOfRangeException(nameof(size)); }
        return new(region, checked(Offset + offset), size);
    }

    private NativeGpuLinearRegion RequireRegion()
        => Region ?? throw new InvalidOperationException("The GPU range is uninitialized.");
}
