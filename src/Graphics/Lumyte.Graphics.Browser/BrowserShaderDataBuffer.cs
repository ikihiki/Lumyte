using System.Diagnostics.CodeAnalysis;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Browser;

internal sealed class BrowserShaderDataBuffer<T> : IGraphicsShaderDataBuffer<T>, IShaderDataSource
    where T : struct, IShaderArguments
{
    private bool _disposed;

    internal BrowserShaderDataBuffer(BrowserDevice owner, ShaderArtifact artifact, ulong count, MemoryPreference memory)
    {
        if (memory is not (MemoryPreference.Automatic or MemoryPreference.Upload))
        {
            throw new ArgumentException("Shader data supports GPU storage or upload staging only.", nameof(memory));
        }

        Layout = ShaderDataLayout.Data<T>(artifact, owner.Caps.ShaderTarget);
        if (count == 0 || count > int.MaxValue || checked(count * (ulong)Layout.Size) > owner.Caps.MaxBufferSize)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        Memory = memory;
        Storage = owner.CreateBuffer<byte>(new() { Count = checked(count * (ulong)Layout.Size), Memory = memory, Usage = memory == MemoryPreference.Upload ? BufferUsage.CopySource : BufferUsage.CopyDestination | BufferUsage.ShaderRead });
        Owner = owner;
        Count = count;
    }

    public ulong Count { get; }

    public ulong SizeInBytes => checked(Count * ShaderElementStrideInBytes);

    public ulong ShaderElementStrideInBytes => (ulong)Layout.Size;

    public IShaderDataLayout Layout { get; }

    public MemoryPreference Memory { get; }

    public bool IsMapped => Storage.IsMapped;

    public object ShaderHandle => ((IShaderRawBuffer)Storage).ShaderHandle;

    internal IGraphicsBuffer<byte> Storage { get; }

    internal BrowserDevice Owner { get; }

    public ValueTask MapAsync(CancellationToken cancellationToken = default)
    {
        ValidateAlive();
        if (Memory != MemoryPreference.Upload)
        {
            throw new InvalidOperationException("Only upload staging can be mapped.");
        }

        return Storage.MapAsync(cancellationToken);
    }

    public void Unmap()
    {
        ValidateAlive();
        if (Memory != MemoryPreference.Upload)
        {
            throw new InvalidOperationException("Only upload staging can be unmapped.");
        }

        Storage.Unmap();
    }

    public void CopyFrom(ReadOnlySpan<T> source, ulong elementOffset = 0)
    {
        ValidateAlive();
        if (source.IsEmpty)
        {
            if (Memory != MemoryPreference.Upload || !IsMapped)
            {
                throw new InvalidOperationException("CopyFrom requires mapped upload staging.");
            }

            if (elementOffset > Count)
            {
                throw new ArgumentOutOfRangeException(nameof(elementOffset));
            }

            return;
        }

        BrowserShaderTransfer.CopyFrom(Owner, source, Storage, elementOffset, Count);
    }

    public void CopyFrom<TSource>(ReadOnlySpan<TSource> source, ulong elementOffset = 0)
        where TSource : struct, IShaderArguments
    {
        ValidateAlive();
        ShaderTransfer.ValidateCompatibility<T, TSource>();
        if (source.IsEmpty)
        {
            if (Memory != MemoryPreference.Upload || !IsMapped)
            {
                throw new InvalidOperationException("CopyFrom requires mapped upload staging.");
            }

            if (elementOffset > Count)
            {
                throw new ArgumentOutOfRangeException(nameof(elementOffset));
            }

            return;
        }

        BrowserShaderTransfer.CopyFrom(Owner, source, Storage, elementOffset, Count);
    }

    public ShaderDataSlice<T> SliceElements(ulong offset, ulong count)
    {
        ValidateAlive();
        return new(this, offset, count);
    }

    public bool TryGetDependencies([NotNullWhen(true)] out IShaderReferenceMetadata? dependencies)
    {
        ValidateAlive();
        dependencies = null;
        return false;
    }

    public void ValidateAlive() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Storage.Dispose();

        _disposed = true;
    }
}
