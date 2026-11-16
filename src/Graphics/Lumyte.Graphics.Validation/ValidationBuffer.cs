using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Validation;

internal sealed class ValidationBuffer<T> : ValidationResource<IGraphicsBuffer<T>>, IGraphicsBuffer<T>, IShaderRawBuffer
    where T : unmanaged
{
    internal ValidationBuffer(ValidationDevice device, IGraphicsBuffer<T> raw)
        : base(device, raw, device)
    {
    }

    public ulong SizeInBytes
    {
        get
        {
            ValidateAlive();
            return Raw.SizeInBytes;
        }
    }

    public BufferLayout<T> Layout
    {
        get
        {
            ValidateAlive();
            return Raw.Layout;
        }
    }

    public ulong Count
    {
        get
        {
            ValidateAlive();
            return Raw.Count;
        }
    }

    public BufferUsage Usage
    {
        get
        {
            ValidateAlive();
            return Raw.Usage;
        }
    }

    public MemoryPreference Memory
    {
        get
        {
            ValidateAlive();
            return Raw.Memory;
        }
    }

    public bool IsMapped
    {
        get
        {
            ValidateAlive();
            return Raw.IsMapped;
        }
    }

    public Type ElementType => typeof(T);

    public string ShaderTypeName
    {
        get
        {
            ValidateAlive();
            return Raw is IShaderRawBuffer metadata ? metadata.ShaderTypeName : typeof(T).Name;
        }
    }

    public object ShaderHandle
    {
        get
        {
            ValidateAlive();
            return Raw is IShaderRawBuffer metadata ? metadata.ShaderHandle : throw new NotSupportedException("Backend does not expose shader storage metadata.");
        }
    }

    public void ValidateByteRange(ulong offset, ulong length)
    {
        ValidateAlive();
        Raw.ValidateByteRange(offset, length);
    }

    public void CopyBytesFrom(ReadOnlySpan<byte> source, ulong offset, ulong length)
    {
        ValidateByteRange(offset, length);
        if ((ulong)source.Length > length)
        {
            throw new ArgumentException("Source exceeds range.", nameof(source));
        }

        ValidateMapping(MemoryPreference.Upload);
        if (source.IsEmpty)
        {
            return;
        }

        _ = checked((int)offset);
        using GraphicsValidation.HostWriteLease lease = Device.Tracker.BeginHostWrite(this, offset, (ulong)source.Length);
        Raw.CopyBytesFrom(source, offset, length);
    }

    public void CopyBytesTo(Span<byte> destination, ulong offset, ulong length)
    {
        ValidateByteRange(offset, length);
        ValidateMapping(MemoryPreference.Readback);
        if ((ulong)destination.Length < length)
        {
            throw new ArgumentException("Destination is too small.", nameof(destination));
        }

        _ = checked((int)offset);
        _ = checked((int)length);
        Device.Tracker.HostAccess(this, ResourceAccess.HostRead, offset, length);
        Raw.CopyBytesTo(destination, offset, length);
    }

    public async ValueTask MapAsync(CancellationToken cancellationToken = default)
    {
        ValidateAlive();
        Device.Tracker.Map(this);
        await Raw.MapAsync(cancellationToken).ConfigureAwait(false);
    }

    public void Unmap()
    {
        ValidateAlive();
        Device.Tracker.ValidateUnmap(this);
        Raw.Unmap();
    }

    public BufferSlice<T> SliceElements(ulong offset, ulong count)
    {
        ValidateAlive();
        return new(this, offset, count);
    }

    public void CopyFrom(ReadOnlySpan<T> source)
    {
        ValidateAlive();
        if ((ulong)source.Length > Count)
        {
            throw new ArgumentException("Source exceeds buffer.", nameof(source));
        }

        ValidateMapping(MemoryPreference.Upload);
        if (source.IsEmpty)
        {
            return;
        }

        ulong length = Layout.GetSizeInBytes((ulong)source.Length);
        if (length > SizeInBytes)
        {
            throw new ArgumentException("Source exceeds allocation.", nameof(source));
        }

        _ = checked((int)length);
        using GraphicsValidation.HostWriteLease lease = Device.Tracker.BeginHostWrite(this, 0, length);
        Raw.CopyFrom(source);
    }

    public void CopyTo(Span<T> destination)
    {
        ValidateMapping(MemoryPreference.Readback);
        if ((ulong)destination.Length < Count)
        {
            throw new ArgumentException("Destination is too small.", nameof(destination));
        }

        _ = checked((int)SizeInBytes);
        Device.Tracker.HostAccess(this, ResourceAccess.HostRead, 0, SizeInBytes);
        Raw.CopyTo(destination);
    }

    private void ValidateMapping(MemoryPreference required)
    {
        ValidateAlive();
        if (!IsMapped || Memory != required)
        {
            throw new InvalidOperationException("Validation: host access requires matching mapped memory.");
        }
    }
}
