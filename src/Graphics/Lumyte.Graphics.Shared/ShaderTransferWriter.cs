using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Shared;

internal struct ShaderTransferWriter<TDestination>(TDestination destination, int size, Func<ShaderValue, byte[]>? encode, Func<IEnumerable<IShaderReference>, IShaderReferenceMetadata>? metadataFactory, ShaderDataLayout? diagnostic) : IShaderValueWriter, IShaderWireSink
    where TDestination : struct, IShaderTransferDestination
{
    private List<ShaderValue>? _legacy;
    private List<IShaderReference>? _dependencies;
    private bool _written;
    private bool _completed;
    private int _referenceCount;

    public readonly bool SupportsWireCursor => encode != null;

    internal readonly IReadOnlyList<ShaderValue>? DeferredFixups => encode == null ? _legacy : null;

    public void WriteBytes<T>(in T value)
        where T : unmanaged
    {
        Begin<T>();
        destination.Write(MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in value, 1)), 0);
    }

    public void WriteWire<TWire, TEmitter>(in TWire wire, ref TEmitter emitter)
        where TWire : unmanaged
        where TEmitter : struct, IShaderReferenceEmitter
    {
        if (!SupportsWireCursor)
        {
            throw new NotSupportedException("This compatibility sink requires the legacy bulk writer.");
        }

        Begin<TWire>();
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(in wire, 1));
        var cursor = new ShaderWireCursor<ShaderTransferWriter<TDestination>>(bytes, ref this);
        emitter.Emit(ref cursor, ordered: true);
        cursor.Complete();
    }

    public void WriteReference<T>(int offset, IGpuRef<T>? value)
    {
        ValidateLegacySlot(offset);

        // Handwritten codecs retain their original emission/getter order. Their
        // private partial output is patched only after the one codec invocation.
        (_legacy ??= []).Add(new(typeof(T), value, offset));
    }

    void IShaderWireSink.WriteRaw(ReadOnlySpan<byte> source, int offset) => destination.Write(source, offset);

    void IShaderWireSink.WriteReference<T>(int offset, IGpuRef<T>? value)
        => EncodeReference(new(typeof(T), value, offset));

    internal void WriteRawValue(ReadOnlySpan<byte> bytes)
    {
        if (_written || bytes.Length != size)
        {
            throw new InvalidOperationException("A transfer must write exactly one complete C# wire value.");
        }

        _written = true;
        destination.Write(bytes, 0);
    }

    internal void WriteCapturedReference(ShaderValue value)
    {
        ValidateLegacySlot(value.Offset);

        (_legacy ??= []).Add(value);
    }

    internal IShaderReferenceMetadata Complete()
    {
        if (!_written || _completed)
        {
            throw new InvalidOperationException("The codec must write exactly one complete C# wire value.");
        }

        _completed = true;
        if (encode == null)
        {
            var compatibility = new ShaderReferenceMetadata(_legacy ?? (IEnumerable<ShaderValue>)Array.Empty<ShaderValue>());
            diagnostic?.ValidateReferenceMetadata(compatibility);
            return compatibility;
        }

        if (_legacy != null)
        {
            foreach (ShaderValue value in _legacy)
            {
                EncodeReference(value);
            }
        }

        diagnostic?.ValidateReferenceCount(_referenceCount);
        IEnumerable<IShaderReference> dependencies = _dependencies ?? (IEnumerable<IShaderReference>)Array.Empty<IShaderReference>();
        return metadataFactory == null ? new ShaderDependencyMetadata(dependencies) : metadataFactory(dependencies);
    }

    private readonly void ValidateLegacySlot(int offset)
    {
        if (_completed || offset < 0 || offset > size - 16)
        {
            throw new ArgumentException("Reference slot exceeds the C# wire value.", nameof(offset));
        }

        if (_legacy == null)
        {
            return;
        }

        foreach (ShaderValue prior in _legacy)
        {
            if ((long)offset < ((long)prior.Offset + 16) && (long)prior.Offset < ((long)offset + 16))
            {
                throw new ArgumentException("Reference slots must be unique and non-overlapping.", nameof(offset));
            }
        }
    }

    private void Begin<T>()
        where T : unmanaged
    {
        if (_written || Unsafe.SizeOf<T>() != size)
        {
            throw new InvalidOperationException("A transfer must write exactly one complete C# wire value.");
        }

        _written = true;
    }

    private void EncodeReference(ShaderValue value)
    {
        diagnostic?.ValidateReference(value);
        byte[] bytes = encode!(value);
        if (bytes.Length != 16)
        {
            throw new ArgumentException("Reference encoding must match the 16-byte ABI.");
        }

        destination.Write(bytes, value.Offset);
        if (value.Reference is not IShaderReference reference)
        {
            throw new ArgumentException("A shader reference must originate from an argument table.");
        }

        (_dependencies ??= []).Add(reference);
        _referenceCount++;
    }
}
