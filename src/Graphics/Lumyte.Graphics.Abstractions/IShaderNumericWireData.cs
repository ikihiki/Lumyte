namespace Lumyte.Graphics.Abstractions;

/// <summary>Explicitly opts a numeric-only C# shader-data structure into direct contiguous wire upload.</summary>
/// <remarks>The C# type must contain no managed references and explicitly define its field offsets and stride.
/// Backends validate this declaration against the shader once and reject incompatible targets rather than repacking.
/// Ordinary IShaderData uses fixed-offset codecs rather than direct CLR storage copying.</remarks>
public interface IShaderNumericWireData : IShaderData
{
    /// <summary>Gets the C#-authoritative immutable numeric wire declaration.</summary>
    ShaderNumericWireLayout NumericWireLayout { get; }
}
