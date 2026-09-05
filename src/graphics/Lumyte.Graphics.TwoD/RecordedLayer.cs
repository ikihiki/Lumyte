namespace Lumyte.Graphics.TwoD;

internal readonly record struct RecordedLayer(
    int Id,
    int ParentId,
    int Sequence,
    LayerOptions Options);
