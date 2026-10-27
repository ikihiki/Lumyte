using System.Numerics;

namespace Lumyte.Graphics.Text.Parley.Tests;

internal static class TestFonts
{
    public static string Add(ParleyTextEngine2D engine, string name)
    {
        using FileStream stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fonts", name));
        return engine.AddFont(stream);
    }

    public static byte AlphaAt(DistanceFieldImage2D image, Vector2 point)
    {
        int x = Math.Clamp((int)((point.X - image.PlaneBounds.X) * image.PixelsPerUnit), 0, image.Width - 1);
        int y = Math.Clamp((int)((point.Y - image.PlaneBounds.Y) * image.PixelsPerUnit), 0, image.Height - 1);
        return image.Pixels.Span[(((y * image.Width) + x) * 4) + 3];
    }
}
