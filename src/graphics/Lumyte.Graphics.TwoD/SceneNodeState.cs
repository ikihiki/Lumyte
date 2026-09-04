using System.Numerics;

namespace Lumyte.Graphics.TwoD;

internal readonly record struct SceneNodeState(
    int Slot,
    uint Generation,
    ulong Revision,
    int Order,
    bool Visible,
    SceneContent? Content,
    Matrix3x2 Transform,
    Rect? Clip);
