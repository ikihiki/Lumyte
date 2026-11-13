namespace Lumyte.Graphics.Abstractions;

/// <summary>Copies a complete unmanaged wire value and records opaque references.</summary>
public interface IShaderValueWriter
{
    /// <summary>Gets a value indicating whether synchronous interleaved wire emission is supported.</summary>
    bool SupportsWireCursor => false;

    /// <summary>Consumes a borrowed wire value and its references synchronously.</summary>
    /// <typeparam name="TWire">The complete unmanaged C# wire type.</typeparam>
    /// <typeparam name="TEmitter">The reference emitter.</typeparam>
    /// <param name="wire">The wire value, which must not be retained beyond this call.</param>
    /// <param name="emitter">The emitter, invoked with ordered references on this path.</param>
    /// <remarks>Implementations must finish the wire cursor before publishing their destination.</remarks>
    void WriteWire<TWire, TEmitter>(in TWire wire, ref TEmitter emitter)
        where TWire : unmanaged
        where TEmitter : struct, IShaderReferenceEmitter
        => throw new NotSupportedException("This writer does not support wire cursors.");

    /// <summary>Copies the complete C# representation, including padding, exactly once.</summary>
    /// <typeparam name="T">The unmanaged C# wire type.</typeparam>
    /// <param name="value">The complete wire value.</param>
    void WriteBytes<T>(in T value)
        where T : unmanaged;

    /// <summary>Records a reference and its C# wire slot offset (always 16 bytes).</summary>
    /// <typeparam name="T">The referenced element type.</typeparam>
    /// <param name="offset">The C# wire slot offset.</param>
    /// <param name="value">The non-owning reference.</param>
    void WriteReference<T>(int offset, IGpuRef<T>? value);
}
