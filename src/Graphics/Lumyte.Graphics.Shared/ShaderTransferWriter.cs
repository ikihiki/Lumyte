using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Shared;

internal struct ShaderTransferWriter<TDestination>(TDestination destination, int size, Func<ShaderValue, byte[]> encode, Func<IEnumerable<IShaderReference>, IShaderReferenceMetadata>? metadataFactory, ShaderDataLayout? diagnostic) : IShaderValueWriter, IShaderWireSink
    where TDestination : struct, IShaderTransferDestination
{
    private List<IShaderReference>? _dependencies;
    private bool _written;
    private bool _completed;
    private int _referenceCount;

    public void WriteWire<TWire, TEmitter>(in TWire wire, ref TEmitter emitter)
        where TWire : unmanaged
        where TEmitter : struct, IShaderReferenceEmitter
    {
        if (_written || _completed || Unsafe.SizeOf<TWire>() != size)
        {
            throw new InvalidOperationException("A transfer must emit exactly one complete C# wire value.");
        }

        _written = true;
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in wire, 1));
        var cursor = new ShaderWireCursor<ShaderTransferWriter<TDestination>>(bytes, ref this);
        emitter.Emit(ref cursor);
        cursor.Complete();
    }

    void IShaderWireSink.WriteRaw(ReadOnlySpan<byte> source, int offset) => destination.Write(source, offset);

    void IShaderWireSink.WriteReference<T>(int offset, IGpuRef<T>? reference)
    {
        var value = new ShaderValue(typeof(T), reference, offset);
        diagnostic?.ValidateReference(value);
        byte[] bytes = encode(value);
        if (bytes.Length != 16)
        {
            throw new ArgumentException("Reference encoding must match the 16-byte ABI.", nameof(encode));
        }

        destination.Write(bytes, offset);
        if (reference is not IShaderReference dependency)
        {
            throw new ArgumentException("A shader reference must originate from an argument table.", nameof(reference));
        }

        (_dependencies ??= []).Add(dependency);
        _referenceCount++;
    }

    internal IShaderReferenceMetadata Complete()
    {
        if (!_written || _completed)
        {
            throw new InvalidOperationException("The codec must emit exactly one complete C# wire value.");
        }

        _completed = true;
        diagnostic?.ValidateReferenceCount(_referenceCount);
        IEnumerable<IShaderReference> dependencies = _dependencies ?? (IEnumerable<IShaderReference>)Array.Empty<IShaderReference>();
        return metadataFactory == null ? new ShaderDependencyMetadata(dependencies) : metadataFactory(dependencies);
    }
}
