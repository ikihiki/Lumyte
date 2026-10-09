using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Wgpu;

internal sealed class WgpuShaderDataBuffer<T> : IGraphicsShaderDataBuffer<T>, IShaderDataSource
    where T : struct, IShaderData
{
    private readonly ShaderValueSnapshot?[] _values;
    private int _registrations;
    private bool _disposed;

    internal WgpuShaderDataBuffer(WgpuDevice owner, ShaderArtifact artifact, ulong count)
    {
        T empty = default;
        ShaderValueSnapshot codec = ShaderCodec<T>.Capture(in empty);
        Layout = ShaderDataLayout.Data(artifact.GetTarget(owner.Caps.ShaderTarget), codec.ShaderTypeName);
        Layout.Validate(codec);
        if (count == 0 || count > int.MaxValue || checked(count * (ulong)Layout.Size) > owner.Caps.MaxBufferSize)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        Owner = owner;
        Count = count;
        _values = new ShaderValueSnapshot?[checked((int)count)];
    }

    public ulong Count { get; }

    public ulong SizeInBytes => checked(Count * ShaderElementStrideInBytes);

    public ulong ShaderElementStrideInBytes => (ulong)Layout.Size;

    public ShaderDataLayout Layout { get; }

    internal WgpuDevice Owner { get; }

    public void CopyFrom(ReadOnlySpan<T> source, ulong elementOffset = 0)
    {
        ValidateAlive();
        if (elementOffset > Count || (ulong)source.Length > Count - elementOffset)
        {
            throw new ArgumentOutOfRangeException(nameof(elementOffset));
        }

        var snapshots = new ShaderValueSnapshot[source.Length];
        for (int i = 0; i < source.Length; i++)
        {
            snapshots[i] = ShaderCodec<T>.Capture(in source[i]);
            Layout.Validate(snapshots[i]);
            foreach (ShaderValue value in snapshots[i].Values)
            {
                if (value.IsReference && (value.Reference is not IShaderReference reference || reference.Table is not WgpuArgumentTable table || !table.BelongsTo(Owner)))
                {
                    throw new ArgumentException("Shader data requires live references from the same device.", nameof(source));
                }

                (value.Reference as IShaderReference)?.Validate();
            }
        }

        snapshots.CopyTo(_values, checked((int)elementOffset));
    }

    public ShaderDataSlice<T> SliceElements(ulong offset, ulong count)
    {
        ValidateAlive();
        return new(this, offset, count);
    }

    public ShaderValueSnapshot Read(ulong index)
    {
        ValidateAlive();
        if (index >= Count)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        return _values[checked((int)index)] ?? throw new InvalidOperationException("Shader data element has not been set by the CPU.");
    }

    public void ValidateAlive() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_registrations != 0)
        {
            throw new InvalidOperationException("Release shader data registrations before disposing the buffer.");
        }

        Array.Clear(_values);
        _disposed = true;
        Owner.ReleaseBuffer();
    }

    internal void RetainRegistration()
    {
        ValidateAlive();
        _registrations++;
    }

    internal void ReleaseRegistration() => _registrations--;
}
