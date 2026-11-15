using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation;

internal sealed class ValidationArgumentTable : ValidationResource<IArgumentTable>, IArgumentTable
{
    private readonly Dictionary<(int Kind, uint Slot), object> _revisions = [];

    internal ValidationArgumentTable(ValidationDevice device, IArgumentTable raw)
        : base(device, raw, device)
    {
    }

    public uint TextureCapacity
    {
        get
        {
            ValidateAlive();
            return Raw.TextureCapacity;
        }
    }

    public uint SamplerCapacity
    {
        get
        {
            ValidateAlive();
            return Raw.SamplerCapacity;
        }
    }

    public uint BufferCapacity
    {
        get
        {
            ValidateAlive();
            return Raw.BufferCapacity;
        }
    }

    public IGpuRef<IGraphicsTextureView> WriteTexture(uint slot, IGraphicsTextureView view)
    {
        ValidateAlive();
        IGraphicsTextureView raw = Device.Unwrap(view);
        return new ValidationGpuRef<IGraphicsTextureView, IGraphicsTextureView>(this, Raw.WriteTexture(slot, raw), view, Commit(0, slot), 0);
    }

    public IGpuRef<IGraphicsSampler> WriteSampler(uint slot, IGraphicsSampler sampler)
    {
        ValidateAlive();
        IGraphicsSampler raw = Device.Unwrap(sampler);
        return new ValidationGpuRef<IGraphicsSampler, IGraphicsSampler>(this, Raw.WriteSampler(slot, raw), sampler, Commit(1, slot), 1);
    }

    public IGpuRef<T> WriteBuffer<T>(uint slot, BufferSlice<T> range, ShaderBufferAccess access = ShaderBufferAccess.ReadOnly)
        where T : unmanaged
    {
        ValidateAlive();
        IGraphicsBuffer<T> raw = Device.Unwrap(range.Buffer);
        return new ValidationGpuRef<T, T>(this, Raw.WriteBuffer(slot, new BufferSlice<T>(raw, range.Offset, range.Count), access), range.Buffer, Commit(2, slot), 2);
    }

    public IGpuRef<T> WriteBuffer<T>(uint slot, ShaderDataSlice<T> range)
        where T : struct, IShaderArguments
    {
        ValidateAlive();
        Device.UnwrapObject(range.Buffer);
        if (range.Buffer is not ValidationShaderDataBuffer<T> buffer)
        {
            throw new ArgumentException("Invalid shader buffer.", nameof(range));
        }

        var nativeRange = new ShaderDataSlice<ValidationArguments<T>>(buffer.Raw, range.Offset, range.Count);
        return new ValidationGpuRef<T, ValidationArguments<T>>(this, Raw.WriteBuffer(slot, nativeRange), buffer, Commit(2, slot), 2);
    }

    public void ReleaseTexture(uint slot)
    {
        ValidateAlive();
        Raw.ReleaseTexture(slot);
        _revisions.Remove((0, slot));
    }

    public void ReleaseSampler(uint slot)
    {
        ValidateAlive();
        Raw.ReleaseSampler(slot);
        _revisions.Remove((1, slot));
    }

    public void ReleaseBuffer(uint slot)
    {
        ValidateAlive();
        Raw.ReleaseBuffer(slot);
        _revisions.Remove((2, slot));
    }

    internal void ValidateRevision(int kind, uint slot, object revision)
    {
        if (!_revisions.TryGetValue((kind, slot), out object? current) || !ReferenceEquals(current, revision))
        {
            throw new InvalidOperationException("Validation: stale argument registration.");
        }
    }

    private object Commit(int kind, uint slot)
    {
        object revision = new();
        _revisions[(kind, slot)] = revision;
        return revision;
    }
}
