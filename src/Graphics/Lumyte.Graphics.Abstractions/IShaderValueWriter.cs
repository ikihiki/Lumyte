namespace Lumyte.Graphics.Abstractions;

/// <summary>Consumes a complete unmanaged wire value through a synchronous cursor.</summary>
public interface IShaderValueWriter
{
    /// <summary>Consumes a borrowed wire value and its references synchronously.</summary>
    /// <typeparam name="TWire">The complete unmanaged C# wire type.</typeparam>
    /// <typeparam name="TEmitter">The reference emitter.</typeparam>
    /// <param name="wire">The wire value, which must not be retained beyond this call.</param>
    /// <param name="emitter">The emitter of physically ascending, disjoint reference slots.</param>
    /// <remarks>Implementations must finish the wire cursor before publishing their destination.
    /// Raw ranges, including padding, must be copied without per-field serialization.</remarks>
    void WriteWire<TWire, TEmitter>(in TWire wire, ref TEmitter emitter)
        where TWire : unmanaged
        where TEmitter : struct, IShaderReferenceEmitter;
}
