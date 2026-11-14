using System.Diagnostics.CodeAnalysis;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Vulkan;

internal sealed class VulkanShaderDataBuffer<T> : IGraphicsShaderDataBuffer<T>, IShaderDataSource, IValidatedShaderDataStorage, IVulkanShaderBuffer, IVulkanDependencyStorage
    where T : struct, IShaderArguments
{
    private readonly DependencyState? _dependencies;
    private readonly ShaderDataLayout? _diagnosticLayout;
    private bool _disposed;

    internal VulkanShaderDataBuffer(VulkanDevice owner, ShaderArtifact artifact, ulong count, MemoryPreference memory)
    {
        if (memory is not (MemoryPreference.Automatic or MemoryPreference.Upload))
        {
            throw new ArgumentException("Shader data supports GPU storage or upload staging only.", nameof(memory));
        }

        Layout = ShaderDataLayout.Data<T>(artifact, owner.Caps.ShaderTarget);
        _diagnosticLayout = owner.Validation == null ? null : ShaderDataLayout.Data(artifact, owner.Caps.ShaderTarget, T.ShaderTypeName);
        if (count == 0 || count > int.MaxValue || checked(count * (ulong)Layout.Size) > owner.Caps.MaxBufferSize)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        Memory = memory;
        Storage = owner.CreateBuffer<byte>(new() { Count = checked(count * (ulong)Layout.Size), Memory = memory, Usage = memory == MemoryPreference.Upload ? BufferUsage.CopySource : BufferUsage.CopyDestination | BufferUsage.ShaderRead });
        Owner = owner;
        Count = count;
        _dependencies = owner.Validation == null ? null : new DependencyState(T.ReferenceCount == 0);
    }

    object IValidatedShaderDataStorage.ValidationStorage => Storage;

    public ulong Count { get; }

    public ulong SizeInBytes => checked(Count * ShaderElementStrideInBytes);

    public ulong ShaderElementStrideInBytes => (ulong)Layout.Size;

    public IShaderDataLayout Layout { get; }

    public MemoryPreference Memory { get; }

    public bool IsMapped => Storage.IsMapped;

    ulong IVulkanShaderBuffer.ShaderAddress
    {
        get
        {
            ValidateAlive();
            return ((IVulkanShaderBuffer)Storage).ShaderAddress;
        }
    }

    public object ShaderHandle => ((IShaderRawBuffer)Storage).ShaderHandle;

    internal IGraphicsBuffer<byte> Storage { get; }

    internal VulkanDevice Owner { get; }

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

        HashSet<IShaderReference>? collector = _dependencies == null ? null : new();
        try
        {
            VulkanShaderTransfer.CopyFrom(Owner, source, Storage, elementOffset, Count, collector, _diagnosticLayout);
            if (_dependencies != null)
            {
                bool full = elementOffset == 0 && (ulong)source.Length == Count;
                if (full)
                {
                    _dependencies.Revision = VulkanReferenceMetadata.TakeOwnership(collector!.ToArray());
                }
                else if (_dependencies.Revision is { } previous)
                {
                    _dependencies.Revision = VulkanReferenceMetadata.Merge(previous, VulkanReferenceMetadata.TakeOwnership(collector!.ToArray()));
                }
            }
        }
        catch
        {
            if (_dependencies != null)
            {
                _dependencies.Revision = null;
            }

            throw;
        }
    }

    public ShaderDataSlice<T> SliceElements(ulong offset, ulong count)
    {
        ValidateAlive();
        return new(this, offset, count);
    }

    public bool TryGetDependencies([NotNullWhen(true)] out IShaderReferenceMetadata? dependencies)
    {
        ValidateAlive();
        dependencies = _dependencies == null ? null : _dependencies.Revision ?? throw new InvalidOperationException("Shader buffer dependencies are unknown until a complete rewrite.");
        return dependencies != null;
    }

    void IVulkanDependencyStorage.PublishDependencies(IShaderReferenceMetadata dependencies)
    {
        ValidateAlive();
        if (_dependencies == null)
        {
            throw new InvalidOperationException("Dependency tracking is disabled.");
        }

        _dependencies.Revision = dependencies;
    }

    public void ValidateAlive() => ObjectDisposedException.ThrowIf(_disposed, this);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Owner.Validation?.ValidateRelease(Storage);
        Owner.Validation?.ValidateRelease(this);

        Storage.Dispose();
        Owner.Validation?.Release(this);
        if (_dependencies != null)
        {
            _dependencies.Revision = null;
        }

        _disposed = true;
    }

    private sealed class DependencyState(bool knownEmpty)
    {
        internal IShaderReferenceMetadata? Revision { get; set; } = knownEmpty ? VulkanReferenceMetadata.TakeOwnership(Array.Empty<IShaderReference>()) : null;
    }
}
