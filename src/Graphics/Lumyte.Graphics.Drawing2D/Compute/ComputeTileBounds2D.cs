namespace Lumyte.Graphics.Drawing2D;

/// <summary>Signed tile origin and positive extents; default is empty.</summary>
internal readonly record struct ComputeTileBounds2D(int X, int Y, int Width, int Height)
{
    internal long Count => checked((long)Width * Height);
}
