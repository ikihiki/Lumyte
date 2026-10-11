using System.Numerics;

namespace Lumyte.Graphics.Drawing2D;

/// <summary>An immutable drawing-state snapshot.</summary>
/// <param name="Transform">The local-to-root logical transform.</param>
/// <param name="Clip">The intersection of captured geometric clips.</param>
public sealed record DrawingState2D(Matrix3x2 Transform, ClipStack2D Clip);
