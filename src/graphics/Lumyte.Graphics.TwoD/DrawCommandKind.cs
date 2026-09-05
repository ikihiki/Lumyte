namespace Lumyte.Graphics.TwoD;

internal enum DrawCommandKind : uint
{
    Rectangle = 1,
    RoundedRectangle = 2,
    Ellipse = 3,
    Line = 4,
    Image = 5,
    Polygon = 6,
    DistanceField = 7,
    Path = 8,
}
