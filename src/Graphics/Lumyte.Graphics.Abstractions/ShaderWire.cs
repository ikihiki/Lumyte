namespace Lumyte.Graphics.Abstractions;

/// <summary>Dispatches a borrowed wire value to an opt-in cursor writer or a legacy bulk writer.</summary>
public static class ShaderWire
{
    /// <summary>Writes one wire value and synchronously emits its opaque references.</summary>
    /// <typeparam name="TWire">The complete unmanaged C# wire type.</typeparam>
    /// <typeparam name="TEmitter">The generated synchronous reference provider.</typeparam>
    /// <typeparam name="TWriter">The destination writer.</typeparam>
    /// <param name="wire">The wire value, borrowed for this call only.</param>
    /// <param name="emitter">The synchronous reference emitter.</param>
    /// <param name="writer">The mutable sink state passed by reference.</param>
    public static void Write<TWire, TEmitter, TWriter>(in TWire wire, ref TEmitter emitter, ref TWriter writer)
        where TWire : unmanaged
        where TEmitter : struct, IShaderReferenceEmitter
        where TWriter : IShaderValueWriter
    {
        if (writer.SupportsWireCursor)
        {
            writer.WriteWire(in wire, ref emitter);
        }
        else
        {
            writer.WriteBytes(in wire);
            var cursor = new LegacyCursor<TWriter>(ref writer);
            emitter.Emit(ref cursor, ordered: false);
        }
    }

    private ref struct LegacyCursor<TWriter>(ref TWriter writer) : IShaderReferenceCursor
        where TWriter : IShaderValueWriter
    {
        private readonly ref TWriter _writer = ref writer;

        public void WriteReference<T>(int offset, IGpuRef<T>? value)
            => _writer.WriteReference(offset, value);
    }
}
