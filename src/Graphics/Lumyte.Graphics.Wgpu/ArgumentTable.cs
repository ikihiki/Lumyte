using G = Lumyte.Graphics;

namespace Lumyte.Graphics.Wgpu;

internal sealed class ArgumentTable : GpuResource, IArgumentTable
{
    private readonly Dictionary<uint, DescriptorRegistration> _textures = [];
    private readonly Dictionary<uint, DescriptorRegistration> _samplers = [];
    private readonly Dictionary<uint, DescriptorRegistration> _buffers = [];

    internal ArgumentTable(WgpuDriver driver, WgpuDevice owner, ArgumentTableDesc desc)
        : base(owner)
    {
        (Driver, TextureCapacity, SamplerCapacity, BufferCapacity) = (driver, desc.TextureCapacity, desc.SamplerCapacity, desc.BufferCapacity);
    }

    public uint TextureCapacity { get; }

    public uint SamplerCapacity { get; }

    public uint BufferCapacity { get; }

    private WgpuDriver Driver { get; }

    public IGpuRef<IGraphicsTextureView> WriteTexture(uint slot, IGraphicsTextureView view)
    {
        lock (Owner.Gate)
        {
            Check(Owner);
            if (view is not TextureView resource)
            {
                throw new ArgumentException("Unsupported texture view.", nameof(view));
            }

            resource.Check(Owner);
            if (!resource.Texture.Usage.HasFlag(TextureUsage.Sampled))
            {
                throw new ArgumentException("Sampled usage is required.", nameof(view));
            }

            return new ResourceElementReference<IGraphicsTextureView>(Write(_textures, slot, TextureCapacity, resource), 1);
        }
    }

    public IGpuRef<G.Sampler> WriteSampler(uint slot, G.Sampler sampler)
    {
        lock (Owner.Gate)
        {
            Check(Owner);
            ArgumentNullException.ThrowIfNull(sampler);
            if (!ReferenceEquals(sampler.Driver, Driver) || sampler.Handle is not Sampler resource)
            {
                throw new ArgumentException("Sampler belongs to another device.", nameof(sampler));
            }

            resource.Check(Owner);
            return new ResourceElementReference<G.Sampler>(Write(_samplers, slot, SamplerCapacity, resource), 1);
        }
    }

    public IGpuRef<T> WriteBuffer<T>(uint slot, G.BufferSlice<T> range)
        where T : unmanaged
    {
        lock (Owner.Gate)
        {
            Check(Owner);
            G.BufferRange bytes = range.Range;
            if (bytes.Buffer is not WgpuBuffer buffer)
            {
                throw new ArgumentException("Unsupported buffer.", nameof(range));
            }

            buffer.Check(Owner);
            if ((buffer.Usage & (BufferUsage.ShaderRead | BufferUsage.ShaderWrite)) == 0 || bytes.Offset % 4 != 0 || bytes.Length % 4 != 0)
            {
                throw new ArgumentException("Registration requires an aligned shader storage range.", nameof(range));
            }

            return new ResourceElementReference<T>(Write(_buffers, slot, BufferCapacity, buffer, buffer.Slice(bytes.Offset, bytes.Length)), range.Count, bytes.Length / range.Count);
        }
    }

    public IGpuRef<T> WriteBuffer<T>(uint slot, G.BufferSlice<byte> range, ShaderDataLayout<T> layout)
    {
        lock (Owner.Gate)
        {
            Check(Owner);
            ArgumentNullException.ThrowIfNull(layout);
            if (!ReferenceEquals(layout.Driver, Driver))
            {
                throw new ArgumentException("Layout belongs to another device.", nameof(layout));
            }

            var region = (ShaderDataRegion)Driver.CreateShaderDataReference(range.Range, typeof(T));
            if (!ReferenceEquals(region.Snapshot.Schema, layout.Handle))
            {
                throw new ArgumentException("Layout does not match serialized shader data.", nameof(layout));
            }

            var bytes = new BufferSlice(region.Buffer, region.Offset, region.Length);
            DescriptorRegistration registration = Write(_buffers, slot, BufferCapacity, region.Buffer, bytes);
            return new ResourceElementReference<T>(registration, region.Length / layout.ElementStrideInBytes, layout.ElementStrideInBytes, region);
        }
    }

    public void ReleaseTexture(uint slot) => Release(_textures, slot, TextureCapacity);

    public void ReleaseSampler(uint slot) => Release(_samplers, slot, SamplerCapacity);

    public void ReleaseBuffer(uint slot) => Release(_buffers, slot, BufferCapacity);

    protected override void ReleaseNative()
    {
        DescriptorRegistration[] registrations = _textures.Values.Concat(_samplers.Values).Concat(_buffers.Values).ToArray();
        foreach (DescriptorRegistration registration in registrations)
        {
            registration.RequireRegistrationIdle();
        }

        foreach (DescriptorRegistration registration in registrations)
        {
            registration.Dispose();
        }

        _textures.Clear();
        _samplers.Clear();
        _buffers.Clear();
    }

    private static void ValidateSlot(uint slot, uint capacity)
    {
        if (slot >= capacity)
        {
            throw new ArgumentOutOfRangeException(nameof(slot));
        }
    }

    private DescriptorRegistration Write(Dictionary<uint, DescriptorRegistration> entries, uint slot, uint capacity, GpuResource resource, BufferSlice range = default)
    {
        ValidateSlot(slot, capacity);
        entries.TryGetValue(slot, out DescriptorRegistration? previous);
        previous?.RequireRegistrationIdle();
        uint identity = Owner.NextDescriptorIdentity(resource, range);
        var registration = new DescriptorRegistration(Owner, identity, resource, range);
        previous?.Dispose();
        entries[slot] = registration;
        return registration;
    }

    private void Release(Dictionary<uint, DescriptorRegistration> entries, uint slot, uint capacity)
    {
        lock (Owner.Gate)
        {
            Check(Owner);
            ValidateSlot(slot, capacity);
            if (entries.TryGetValue(slot, out DescriptorRegistration? registration))
            {
                registration.Dispose();
                entries.Remove(slot);
            }
        }
    }
}
