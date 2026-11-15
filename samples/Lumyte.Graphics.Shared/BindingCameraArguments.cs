using System.Numerics;

namespace Lumyte.Graphics.Samples;

/// <summary>Defines numeric root values without an argument table.</summary>
/// <param name="ViewProjection">The row-major matrix payload.</param>
public readonly record struct BindingCameraArguments(Matrix4x4 ViewProjection);
