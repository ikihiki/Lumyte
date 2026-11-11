using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Samples;

/// <summary>References persistent matrix data and a writable output range.</summary>
/// <param name="Data">The matrix data element.</param>
/// <param name="Output">The output values.</param>
public readonly partial record struct BindingMatrixArguments(IGpuRef<BindingMatrixData> Data, IGpuRef<uint> Output) : IShaderArguments;
