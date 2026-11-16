using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Vulkan;

internal sealed class VulkanArgumentTable(VulkanDevice owner, ArgumentTableDesc desc) : IArgumentTable
{
    private readonly Dictionary<uint, ArgumentRegistration> _textures = new();
    private readonly Dictionary<uint, ArgumentRegistration> _samplers = new();
    private readonly Dictionary<uint, ArgumentRegistration> _buffers = new();
    private readonly Dictionary<(uint Textures, uint Samplers), VulkanShaderBinding> _sets = [];
    private bool _sealed;
    private long _descriptorGeneration;
    private bool _disposed;

    public uint TextureCapacity { get; } = desc.TextureCapacity;

    public uint SamplerCapacity { get; } = desc.SamplerCapacity;

    public uint BufferCapacity { get; } = desc.BufferCapacity;

    internal long DescriptorGeneration => _descriptorGeneration;

    internal int CachedSetCount => _sets.Count;

    public IGpuRef<IGraphicsTextureView> WriteTexture(uint slot, IGraphicsTextureView view)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(view);
        if (view is not VulkanTextureView resource || !ReferenceEquals(resource.Owner, owner) || (view.Texture.Usage & TextureUsage.Sampled) == 0)
        {
            throw new ArgumentException("A sampled view from the same device is required.", nameof(view));
        }

        return Write<IGraphicsTextureView>(_textures, TextureCapacity, slot, resource, 1, 0, 0, 0);
    }

    public IGpuRef<IGraphicsSampler> WriteSampler(uint slot, IGraphicsSampler sampler)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(sampler);
        if (sampler is not VulkanSampler resource || !ReferenceEquals(resource.Owner, owner))
        {
            throw new ArgumentException("A sampler from the same device is required.", nameof(sampler));
        }

        return Write<IGraphicsSampler>(_samplers, SamplerCapacity, slot, resource, 1, 0, 0, 0);
    }

    public IGpuRef<T> WriteBuffer<T>(uint slot, BufferSlice<T> range, ShaderBufferAccess access = ShaderBufferAccess.ReadOnly)
        where T : unmanaged
    {
        ThrowIfDisposed();
        if (range.Buffer is not VulkanBuffer<T> resource || !ReferenceEquals(resource.Owner, owner) || (resource.Usage & (BufferUsage.ShaderRead | BufferUsage.ShaderWrite)) == 0)
        {
            throw new ArgumentException("A shader buffer range from the same device is required.", nameof(range));
        }

        if (access is not ShaderBufferAccess.ReadOnly and not ShaderBufferAccess.ReadWrite)
        {
            throw new ArgumentOutOfRangeException(nameof(access));
        }

        BufferUsage required = access == ShaderBufferAccess.ReadWrite ? BufferUsage.ShaderWrite : BufferUsage.ShaderRead;
        if ((resource.Usage & required) != required)
        {
            throw new ArgumentException("The buffer usage does not support the selected registration access.", nameof(access));
        }

        resource.ValidateByteRange(range.OffsetInBytes, range.SizeInBytes);
        return Write<T>(_buffers, BufferCapacity, slot, resource, range.Count, range.Buffer.Layout.ElementStrideInBytes, range.OffsetInBytes, range.SizeInBytes, access);
    }

    public IGpuRef<T> WriteBuffer<T>(uint slot, ShaderDataSlice<T> range)
        where T : struct, IShaderArguments
    {
        ThrowIfDisposed();
        if (range.Buffer is not VulkanShaderDataBuffer<T> resource || !ReferenceEquals(resource.Owner, owner) || resource.Memory != MemoryPreference.Automatic)
        {
            throw new ArgumentException("Shader data range belongs to another device.", nameof(range));
        }

        resource.ValidateAlive();
        ulong stride = resource.ShaderElementStrideInBytes;
        return Write<T>(_buffers, BufferCapacity, slot, resource, range.Count, stride, checked(range.Offset * stride), checked(range.Count * stride));
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

        DropSets();
        _textures.Clear();
        _samplers.Clear();
        _buffers.Clear();
        _disposed = true;
    }

    internal IShaderReference[] SnapshotRegistrations()
    {
        ThrowIfDisposed();
        return _textures.Values.Concat(_samplers.Values).Concat(_buffers.Values).Cast<IShaderReference>().ToArray();
    }

    internal bool BelongsTo(VulkanDevice device) => ReferenceEquals(owner, device);

    internal void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    internal VulkanShaderBinding AcquireBinding(IShaderReference[] registrations, Silk.NET.Vulkan.DescriptorSetLayout layout)
    {
        ThrowIfDisposed();
        VulkanShaderBinding.ValidateResources(registrations);
        uint textures = registrations.Where(r => r.Resource is VulkanTextureView).Select(r => checked(r.Slot + 1)).DefaultIfEmpty(1u).Max();
        uint samplers = registrations.Where(r => r.Resource is VulkanSampler).Select(r => checked(r.Slot + 1)).DefaultIfEmpty(1u).Max();

        // Every layout is manufactured by CreateLayout with exactly this canonical shape,
        // stage flags and binding flags. Native handle identity is deliberately irrelevant.
        (uint, uint) key = (textures, samplers);
        if (!_sets.TryGetValue(key, out VulkanShaderBinding? binding))
        {
            binding = new(owner, this, registrations, layout);
            try
            {
                _sets.Add(key, binding);
            }
            catch
            {
                binding.Dispose();
                throw;
            }
        }

        _sealed = true;
        return binding;
    }

    private void MutateDescriptors()
    {
        if (_sealed)
        {
            _descriptorGeneration++;
            _sealed = false;
            DropSets();
        }
    }

    private void DropSets()
    {
        try
        {
            foreach (VulkanShaderBinding binding in _sets.Values)
            {
                binding.Dispose();
            }
        }
        finally
        {
            _sets.Clear();
        }
    }

    private IGpuRef<T> Write<T>(Dictionary<uint, ArgumentRegistration> slots, uint capacity, uint slot, object resource, ulong count, ulong stride, ulong offset, ulong size, ShaderBufferAccess access = ShaderBufferAccess.ReadOnly)
    {
        ValidateSlot(capacity, slot);
        var entry = new ArgumentRegistration(this, slot, resource, offset, size, count, access);
        var reference = new GpuReference<T>(entry, count, stride, offset, size);
        slots[slot] = entry;
        if (!ReferenceEquals(slots, _buffers))
        {
            MutateDescriptors();
        }

        return reference;
    }

    private void Release(Dictionary<uint, ArgumentRegistration> slots, uint capacity, uint slot)
    {
        ThrowIfDisposed();
        ValidateSlot(capacity, slot);
        if (slots.Remove(slot) && !ReferenceEquals(slots, _buffers))
        {
            MutateDescriptors();
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
