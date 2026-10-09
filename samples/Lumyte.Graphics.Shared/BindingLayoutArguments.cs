using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Samples;

/// <summary>Selects shader data arrays with independently checked layouts.</summary>
/// <param name="Vectors">The three-component vector array.</param>
/// <param name="Packed">The scalar and vector array.</param>
/// <param name="Nested">The nested structure array.</param>
/// <param name="Output">The writable output values.</param>
[ShaderArguments]
public readonly record struct BindingLayoutArguments(IGpuRef<BindingVector> Vectors, IGpuRef<BindingPackedVector> Packed, IGpuRef<BindingNestedVector> Nested, IGpuRef<uint> Output);
