using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Samples;

/// <summary>Checks scalar and vector member offsets in shader data.</summary>
/// <param name="Id">The scalar prefix.</param>
/// <param name="Color">The vector payload.</param>
public readonly partial record struct BindingPackedVector(uint Id, Vector3 Color) : IShaderData;
