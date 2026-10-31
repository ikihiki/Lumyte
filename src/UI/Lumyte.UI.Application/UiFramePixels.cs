using Lumyte.Graphics.Drawing2D;

namespace Lumyte.UI.Application;

internal sealed record UiFramePixels(uint Width, uint Height, byte[] Pixels)
{
    internal byte[] Crop(Rect2D bounds)
    {
        int x = Math.Clamp((int)Math.Floor(bounds.X), 0, checked((int)Width));
        int y = Math.Clamp((int)Math.Floor(bounds.Y), 0, checked((int)Height));
        int right = Math.Clamp((int)Math.Ceiling(bounds.X + bounds.Width), x, checked((int)Width));
        int bottom = Math.Clamp((int)Math.Ceiling(bounds.Y + bounds.Height), y, checked((int)Height));
        int width = right - x;
        int height = bottom - y;
        if (width == 0 || height == 0)
        {
            throw new NotSupportedException("The target has no visible pixels.");
        }

        byte[] crop = new byte[checked(width * height * 4)];
        for (int row = 0; row < height; row++)
        {
            Pixels.AsSpan(checked((((y + row) * (int)Width) + x) * 4), width * 4).CopyTo(crop.AsSpan(row * width * 4));
        }

        return UiPng.Encode((uint)width, (uint)height, crop);
    }
}
