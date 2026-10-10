using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Samples;

/// <summary>Checks the layout of a nested vector followed by a scalar.</summary>
/// <param name="Child">The nested shader value.</param>
/// <param name="Tail">The trailing scalar.</param>
public readonly partial record struct BindingNestedVector(BindingVector Child, uint Tail) : IShaderData;
