namespace Lumyte.Graphics.TwoD;

internal readonly record struct DistanceFieldEntry(
    int Slot,
    uint Generation,
    AtlasRectangle Region,
    float DistanceRange,
    DistanceFieldEncoding Encoding);
