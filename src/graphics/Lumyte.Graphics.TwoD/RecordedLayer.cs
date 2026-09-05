namespace Lumyte.Graphics.TwoD;

internal readonly record struct RecordedLayer(
    int Id,
    int ParentId,
    int Sequence,
    LayerOptions Options,
    Rect? Clip,
    RecordedClipStack? ClipStack,
    bool ClippedOut);
