using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal sealed class BrowserArgumentTable : IArgumentTable
{
    private readonly BrowserDevice _owner;
    private readonly Dictionary<uint, ArgumentRegistration> _textures = new();
    private readonly Dictionary<uint, ArgumentRegistration> _samplers = new();
    private readonly Dictionary<uint, ArgumentRegistration> _buffers = new();
    private bool _disposed;

    internal BrowserArgumentTable(BrowserDevice owner, ArgumentTableDesc desc)
    {
        (_owner, TextureCapacity, SamplerCapacity, BufferCapacity) = (owner, desc.TextureCapacity, desc.SamplerCapacity, desc.BufferCapacity);
    }

    public uint TextureCapacity { get; }

    public uint SamplerCapacity { get; }

    public uint BufferCapacity { get; }

    public IGpuRef<IGraphicsTextureView> WriteTexture(uint slot, IGraphicsTextureView view)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(view);
        if (view is not BrowserTextureView resource || !ReferenceEquals(resource.Owner, _owner) || (view.Texture.Usage & TextureUsage.Sampled) == 0)
        {
            throw new ArgumentException("A sampled view from the same device is required.", nameof(view));
        }

        return Write<IGraphicsTextureView>(_textures, TextureCapacity, slot, resource, resource.RetainRegistration, resource.ReleaseRegistration, 1, 0, 0, 0);
    }

    public IGpuRef<IGraphicsSampler> WriteSampler(uint slot, IGraphicsSampler sampler)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(sampler);
        if (sampler is not BrowserSampler resource || !ReferenceEquals(resource.Owner, _owner))
        {
            throw new ArgumentException("A sampler from the same device is required.", nameof(sampler));
        }

        return Write<IGraphicsSampler>(_samplers, SamplerCapacity, slot, resource, resource.RetainRegistration, resource.ReleaseRegistration, 1, 0, 0, 0);
    }

    public IGpuRef<T> WriteBuffer<T>(uint slot, BufferSlice<T> range)
        where T : unmanaged
    {
        ThrowIfDisposed();
        if (range.Buffer is not BrowserBuffer<T> resource || !ReferenceEquals(resource.Owner, _owner) || (resource.Usage & (BufferUsage.ShaderRead | BufferUsage.ShaderWrite)) == 0)
        {
            throw new ArgumentException("A shader buffer range from the same device is required.", nameof(range));
        }

        resource.ValidateRange(range.OffsetInBytes, range.SizeInBytes);
        return Write<T>(_buffers, BufferCapacity, slot, resource, resource.RetainRegistration, resource.ReleaseRegistration, range.Count, range.Buffer.Layout.ElementStrideInBytes, range.OffsetInBytes, range.SizeInBytes);
    }

    public void ReleaseTexture(uint slot) => Release(_textures, TextureCapacity, slot);

    public void ReleaseSampler(uint slot) => Release(_samplers, SamplerCapacity, slot);

    public void ReleaseBuffer(uint slot) => Release(_buffers, BufferCapacity, slot);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (ArgumentRegistration entry in _textures.Values.Concat(_samplers.Values).Concat(_buffers.Values))
        {
            entry.Release();
        }

        _textures.Clear();
        _samplers.Clear();
        _buffers.Clear();
        _disposed = true;
        _owner.ReleaseArgumentTable();
    }

    internal void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private IGpuRef<T> Write<T>(Dictionary<uint, ArgumentRegistration> slots, uint capacity, uint slot, object resource, Action retain, Action release, ulong count, ulong stride, ulong offset, ulong size)
    {
        ValidateSlot(capacity, slot);
        var entry = new ArgumentRegistration(this, resource, release);
        var reference = new GpuReference<T>(entry, count, stride, offset, size);
        retain();
        try
        {
            slots.TryGetValue(slot, out ArgumentRegistration? old);
            slots[slot] = entry;
            old?.Release();
            return reference;
        }
        catch
        {
            entry.Release();
            throw;
        }
    }

    private void Release(Dictionary<uint, ArgumentRegistration> slots, uint capacity, uint slot)
    {
        ThrowIfDisposed();
        ValidateSlot(capacity, slot);
        if (slots.Remove(slot, out ArgumentRegistration? entry))
        {
            entry.Release();
        }
    }

    private void ValidateSlot(uint capacity, uint slot)
    {
        ThrowIfDisposed();
        if (slot >= capacity)
        {
            throw new ArgumentOutOfRangeException(nameof(slot));
        }
    }
}
