using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Lumyte.Pipelines;

/// <summary>Owns an immutable copy of canonical unmanaged elements.</summary>
/// <typeparam name="TS">The canonical schema.</typeparam>
public sealed class SchemaData<TS>
    where TS : ISchema
{
    private readonly byte[] _bytes;

    internal SchemaData(byte[] bytes, SchemaExtent extent, object invocation, ICanonicalStorage storage, SchemaDescriptor descriptor)
    {
        _bytes = bytes;
        Extent = extent;
        Invocation = invocation;
        Storage = storage;
        Descriptor = descriptor;
    }

    /// <summary>Gets the immutable extent.</summary>
    public SchemaExtent Extent { get; }

    internal SchemaDescriptor Descriptor { get; }

    internal object Invocation { get; }

    internal ICanonicalStorage Storage { get; }

    /// <summary>Reads elements with exactly the canonical element type and layout.</summary>
    /// <typeparam name="T">The unmanaged canonical element type.</typeparam>
    /// <returns>The immutable borrowed elements.</returns>
    public ReadOnlySpan<T> ReadElements<T>()
        where T : unmanaged
    {
        if (Descriptor.ElementType != typeof(T) || Descriptor.ElementStride != Unsafe.SizeOf<T>())
        {
            throw new InvalidOperationException("The requested element layout does not match the schema.");
        }

        return MemoryMarshal.Cast<byte, T>(_bytes);
    }
}
