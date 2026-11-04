using System.Numerics;

namespace Lumyte.Graphics.Drawing2D;

internal readonly record struct DrawingSceneEntry2D(
    DrawingNodeId2D Node,
    DrawList2D Content,
    Matrix3x2 Transform,
    IReadOnlyList<DrawingSceneClip2D> Clips,
    ulong ContentRevision,
    ulong PropertyRevision);
