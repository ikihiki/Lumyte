using System.Numerics;
using Lumyte.Graphics.Abstractions;

namespace Lumyte.Graphics.Drawing2D;

internal readonly partial record struct SolidRectangleArguments2D(IGpuRef<Vector4> Data, Vector2 Size) : IShaderArguments;
