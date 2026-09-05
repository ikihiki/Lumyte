namespace Lumyte.Graphics.TwoD;

internal readonly record struct PreparedLayer(
    int Id,
    int ParentId,
    int Sequence,
    LayerOptions Options,
    int MaskImageIndex,
    ulong MainParametersOffset,
    ulong ShadowParametersOffset,
    ulong HorizontalBlurParametersOffset,
    ulong VerticalBlurParametersOffset,
    ulong ShadowHorizontalBlurParametersOffset,
    ulong ShadowVerticalBlurParametersOffset);
