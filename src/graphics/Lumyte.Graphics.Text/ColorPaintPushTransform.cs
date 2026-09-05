using System.Numerics;

namespace Lumyte.Graphics.Text;

/// <summary>Pushes an affine transform onto the COLRv1 paint stack.</summary>
internal sealed record ColorPaintPushTransform(Matrix3x2 Transform) : ColorPaintOperation;
