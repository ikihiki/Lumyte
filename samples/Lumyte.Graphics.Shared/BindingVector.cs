using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Samples;

/// <summary>Checks the element stride of a three-component shader value.</summary>
/// <param name="Value">The vector payload.</param>
public readonly record struct BindingVector(Vector3 Value) : IShaderData;
