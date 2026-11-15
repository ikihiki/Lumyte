using System.Diagnostics.CodeAnalysis;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;

namespace Lumyte.Graphics.Validation;

internal sealed class ValidationShaderDataBuffer<T> : ValidationResource<IGraphicsShaderDataBuffer<ValidationArguments<T>>>, IGraphicsShaderDataBuffer<T>, IShaderDataSource
    where T : struct, IShaderArguments
{
    private readonly ShaderDataLayout _diagnostic;
    private IShaderReferenceMetadata _dependencies = new ShaderDependencyMetadata([]);
    private bool _dependenciesValid = T.ReferenceCount == 0;

    internal ValidationShaderDataBuffer(ValidationDevice device, IGraphicsShaderDataBuffer<ValidationArguments<T>> raw, ShaderDataLayout diagnostic)
        : base(device, raw, device)
    {
        _diagnostic = diagnostic;
    }

    public ulong Count
    {
        get
        {
            ValidateAlive();
            return Raw.Count;
        }
    }

    public ulong SizeInBytes
    {
        get
        {
            ValidateAlive();
            return Raw.SizeInBytes;
        }
    }

    public ulong ShaderElementStrideInBytes
    {
        get
        {
            ValidateAlive();
            return Raw.ShaderElementStrideInBytes;
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

    public IShaderDataLayout Layout
    {
        get
        {
            ValidateAlive();
            return ((IShaderDataSource)Raw).Layout;
        }
    }

    public object ShaderHandle
    {
        get
        {
            ValidateAlive();
            return ((IShaderDataSource)Raw).ShaderHandle;
        }
    }

    public bool TryGetDependencies([NotNullWhen(true)] out IShaderReferenceMetadata? dependencies)
    {
        ValidateAlive();
        if (!_dependenciesValid)
        {
            throw new InvalidOperationException("Validation: dependencies are unknown after a failed write; rewrite the complete buffer.");
        }

        dependencies = _dependencies;
        return true;
    }

    public void CopyFrom(ReadOnlySpan<T> source, ulong elementOffset = 0)
    {
        ValidateAlive();
        if (elementOffset > Count || (ulong)source.Length > Count - elementOffset)
        {
            throw new ArgumentOutOfRangeException(nameof(elementOffset));
        }

        if (!IsMapped || Memory != MemoryPreference.Upload)
        {
            throw new InvalidOperationException("Validation: writes require mapped Upload memory.");
        }

        ulong offset = checked(elementOffset * ShaderElementStrideInBytes);
        ulong length = checked((ulong)source.Length * ShaderElementStrideInBytes);
        if (offset > SizeInBytes || length > SizeInBytes - offset)
        {
            throw new ArgumentOutOfRangeException(nameof(elementOffset));
        }

        if (source.IsEmpty)
        {
            return;
        }

        _ = checked((int)offset);
        _ = checked((int)length);
        using GraphicsValidation.HostWriteLease lease = Device.Tracker.BeginHostWrite(this, offset, length);
        bool full = elementOffset == 0 && (ulong)source.Length == Count;
        var context = new ValidationWriteContext(Device, _diagnostic);
        var native = new ValidationArguments<T>[source.Length];
        for (int i = 0; i < source.Length; ++i)
        {
            native[i] = new ValidationArguments<T>(in source[i], context);
        }

        try
        {
            Raw.CopyFrom(native, elementOffset);
            if (full || _dependenciesValid)
            {
                IEnumerable<IShaderReference> references = full ? context.References : _dependencies.References.Concat(context.References);
                _dependencies = new ShaderDependencyMetadata(references.Distinct<IShaderReference>(ReferenceEqualityComparer.Instance));
                _dependenciesValid = true;
            }
        }
        catch
        {
            _dependenciesValid = false;
            throw;
        }
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

    public ShaderDataSlice<T> SliceElements(ulong offset, ulong count)
    {
        ValidateAlive();
        return new(this, offset, count);
    }

    internal void PublishDependencies(IShaderReferenceMetadata dependencies)
    {
        ValidateAlive();
        _dependencies = dependencies;
        _dependenciesValid = true;
    }

    internal void InvalidateDependencies() => _dependenciesValid = false;
}
