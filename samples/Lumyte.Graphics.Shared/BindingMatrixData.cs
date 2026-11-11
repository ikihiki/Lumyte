using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Samples;

/// <summary>Combines persistent matrix, nested numeric and recursive buffer reference data.</summary>
/// <param name="Transform">The row-major transformation.</param>
/// <param name="Child">The nested vector.</param>
/// <param name="Node">The independently tracked linked node.</param>
public readonly partial record struct BindingMatrixData(Matrix4x4 Transform, BindingVector Child, IGpuRef<BindingNode> Node) : IShaderData;
