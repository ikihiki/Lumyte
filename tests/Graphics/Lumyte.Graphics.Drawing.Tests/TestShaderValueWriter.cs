// Test-only streaming fake; never used by an engine backend.
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Shared;

namespace Lumyte.Graphics.Drawing.Tests;

internal struct TestShaderValueWriter<TDestination>(TDestination destination, int size, Func<ShaderValue, byte[]> encode, Func<IEnumerable<IShaderReference>, IShaderReferenceMetadata>? metadataFactory, ShaderDataLayout? diagnostic) : IShaderValueWriter
    where TDestination : struct, ITestShaderTransferDestination
{
    private List<IShaderReference>? _dependencies;
    private bool _written;
    private bool _completed;
    private int _position;
    private int _referenceCount;

    public void WriteValue(ReadOnlySpan<byte> value)
    {
        ValidateWrite(value.Length);
        destination.Write(value, _position);
        _position += value.Length;
        _written = true;
    }

    public void WriteReference<T>(IGpuRef<T>? reference)
    {
        ValidateWrite(16);
        var value = new ShaderValue(typeof(T), reference, _position);
        diagnostic?.ValidateReference(value);
        byte[] bytes = encode(value);
        if (bytes.Length != 16)
        {
            throw new ArgumentException("Reference encoding must match the 16-byte ABI.", nameof(encode));
        }

        destination.Write(bytes, _position);
        if (reference is not IShaderReference dependency)
        {
            throw new ArgumentException("A shader reference must originate from an argument table.", nameof(reference));
        }

        (_dependencies ??= []).Add(dependency);
        _referenceCount++;
        _position += 16;
        _written = true;
    }

    internal IShaderReferenceMetadata Complete()
    {
        if (!_written || _completed || _position != size)
        {
            throw new InvalidOperationException("The codec must write exactly the complete C# wire value.");
        }

        _completed = true;
        diagnostic?.ValidateReferenceCount(_referenceCount);
        IEnumerable<IShaderReference> dependencies = _dependencies ?? (IEnumerable<IShaderReference>)Array.Empty<IShaderReference>();
        return metadataFactory == null ? new ShaderDependencyMetadata(dependencies) : metadataFactory(dependencies);
    }

    private readonly void ValidateWrite(int length)
    {
        if (_completed)
        {
            throw new InvalidOperationException("The transfer is already complete.");
        }

        if (size < 0 || _position > size || length > size - _position)
        {
            throw new ArgumentOutOfRangeException(nameof(length), "The value exceeds the C# wire size.");
        }
    }
}
