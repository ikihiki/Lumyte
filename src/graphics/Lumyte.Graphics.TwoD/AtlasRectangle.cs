namespace Lumyte.Graphics.TwoD;

internal readonly record struct AtlasRectangle(uint X, uint Y, uint Width, uint Height)
{
    public ulong Area => checked((ulong)Width * Height);
}
