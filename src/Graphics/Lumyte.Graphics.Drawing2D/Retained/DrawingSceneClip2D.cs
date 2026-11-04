using System.Numerics;

namespace Lumyte.Graphics.Drawing2D;

internal readonly record struct DrawingSceneClip2D(Path2D Path, FillRule2D Rule, Matrix3x2 Transform);
