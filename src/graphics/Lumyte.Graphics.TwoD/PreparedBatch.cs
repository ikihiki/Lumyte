namespace Lumyte.Graphics.TwoD;

internal readonly record struct PreparedBatch(
    PreparedBatchKind Kind,
    ulong BufferOffset,
    ulong BufferLength,
    uint DrawCount,
    Rect Clip,
    int ImageIndex = -1);
