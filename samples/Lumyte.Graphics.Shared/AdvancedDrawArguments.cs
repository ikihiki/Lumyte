using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Samples;

/// <summary>Supplies procedural vertex color and depth.</summary>
/// <param name="Color">The output color.</param>
/// <param name="Depth">The normalized vertex depth.</param>
public readonly partial record struct AdvancedDrawArguments(Vector4 Color, float Depth) : IShaderArguments;
