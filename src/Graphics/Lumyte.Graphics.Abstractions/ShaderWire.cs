namespace Lumyte.Graphics.Abstractions;

/// <summary>Dispatches borrowed wire values to mandatory synchronous cursor writers.</summary>
public static class ShaderWire
{
    /// <summary>Writes one wire value and synchronously emits its opaque references.</summary>
    /// <typeparam name="TWire">The complete unmanaged C# wire type.</typeparam>
    /// <typeparam name="TEmitter">The synchronous reference provider.</typeparam>
    /// <typeparam name="TWriter">The destination writer.</typeparam>
    /// <param name="wire">The wire value, borrowed for this call only.</param>
    /// <param name="emitter">The synchronous reference emitter.</param>
    /// <param name="writer">The mutable sink state passed by reference.</param>
    public static void Write<TWire, TEmitter, TWriter>(in TWire wire, ref TEmitter emitter, ref TWriter writer)
        where TWire : unmanaged
        where TEmitter : struct, IShaderReferenceEmitter
        where TWriter : IShaderValueWriter
        => writer.WriteWire(in wire, ref emitter);

    /// <summary>Writes a reference-free wire value as one raw cursor range, including padding.</summary>
    /// <typeparam name="TWire">The complete unmanaged C# wire type, containing no reference slots.</typeparam>
    /// <typeparam name="TWriter">The destination writer.</typeparam>
    /// <param name="wire">The raw-only wire value, borrowed for this call only.</param>
    /// <param name="writer">The mutable sink state passed by reference.</param>
    public static void Write<TWire, TWriter>(in TWire wire, ref TWriter writer)
        where TWire : unmanaged
        where TWriter : IShaderValueWriter
    {
        EmptyEmitter emitter = default;
        writer.WriteWire(in wire, ref emitter);
    }

    private readonly struct EmptyEmitter : IShaderReferenceEmitter
    {
        public void Emit<TCursor>(ref TCursor cursor)
            where TCursor : IShaderReferenceCursor, allows ref struct
        {
        }
    }
}
