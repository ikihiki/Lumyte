using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.Graphics.Text.Benchmarks;

internal static class TextPixels
{
    internal const uint Width = 640;
    internal const uint Height = 320;
    internal static readonly Vector2 AnchorOrigin = new(16, 16);
    internal static readonly Vector2 TextOrigin = new(104, 16);

    internal static object Verify(TextCase input, TextLayout2D anchor, byte[] pixels)
    {
        TextChecks.Require(pixels.Length == Width * Height * 8, "Wrong half-float readback dimensions.");
        for (int offset = 0; offset < pixels.Length; offset += 8)
        {
            for (int channel = 0; channel < 4; channel++)
            {
                float value = Channel(pixels, offset + (channel * 2));
                TextChecks.Require(float.IsFinite(value) && value >= -0.004f && value <= 1.004f, "Non-finite/out-of-range text pixel.");
            }

            TextChecks.Require(MathF.Abs(Channel(pixels, offset + 6) - 1) <= 0.004f, "Opaque-white target lost alpha.");
        }

        Rect2D bounds = anchor.Bounds;
        int centerX = (int)(AnchorOrigin.X + bounds.X + (bounds.Width / 2));
        int centerY = (int)(AnchorOrigin.Y + bounds.Y + (bounds.Height / 2));
        TextChecks.Require(Red(pixels, centerX, centerY) > 0.9f, "Known O counter was filled.");
        int anchorInk = 0;
        for (int y = (int)(AnchorOrigin.Y + bounds.Y); y < AnchorOrigin.Y + bounds.Y + bounds.Height; y++)
        {
            for (int x = (int)(AnchorOrigin.X + bounds.X); x < AnchorOrigin.X + bounds.X + bounds.Width; x++)
            {
                if (Red(pixels, x, y) < 0.3f)
                {
                    anchorInk++;
                }
            }
        }

        TextChecks.Require(anchorInk > 40, "Known O stroke/ring was not rendered.");
        TextChecks.Require(Red(pixels, 0, 0) > 0.99f && Red(pixels, (int)Width - 1, (int)Height - 1) > 0.99f, "Clear/bounds guard failed.");
        int mainInk = 0;
        var colors = new HashSet<int>();
        for (int y = 0; y < Height; y++)
        {
            for (int x = (int)TextOrigin.X; x < Width; x++)
            {
                int offset = ((y * (int)Width) + x) * 8;
                float red = Channel(pixels, offset);
                float green = Channel(pixels, offset + 2);
                float blue = Channel(pixels, offset + 4);
                if (MathF.Min(red, MathF.Min(green, blue)) < 0.9f)
                {
                    mainInk++;
                }

                if (MathF.Max(red, MathF.Max(green, blue)) - MathF.Min(red, MathF.Min(green, blue)) > 0.1f)
                {
                    colors.Add(((int)(red * 15) << 8) | ((int)(green * 15) << 4) | (int)(blue * 15));
                }
            }
        }

        TextChecks.Require(mainInk > 10, "Layout glyphs were not visibly rendered.");
        TextChecks.Require(input.Id != "colr" || colors.Count >= 4, "Real COLR gradient colors were lost.");
        return new { status = "passed", anchorInk, mainInk, distinctSaturatedColors = colors.Count, opaqueAlphaTolerance = 0.004 };
    }

    internal static async Task SaveAsync(string prefix, byte[] pixels)
    {
        await File.WriteAllBytesAsync(prefix + ".rgba16f", pixels);
        byte[] header = Encoding.ASCII.GetBytes($"P6\n{Width} {Height}\n255\n");
        byte[] image = new byte[header.Length + ((int)Width * (int)Height * 3)];
        header.CopyTo(image, 0);
        for (int pixel = 0; pixel < Width * Height; pixel++)
        {
            for (int channel = 0; channel < 3; channel++)
            {
                float linear = Math.Clamp(Channel(pixels, (pixel * 8) + (channel * 2)), 0, 1);
                float encoded = linear <= 0.0031308f ? 12.92f * linear : (1.055f * MathF.Pow(linear, 1 / 2.4f)) - 0.055f;
                image[header.Length + (pixel * 3) + channel] = (byte)Math.Clamp((int)MathF.Round(encoded * 255), 0, 255);
            }
        }

        await File.WriteAllBytesAsync(prefix + ".ppm", image);
    }

    private static float Red(byte[] pixels, int x, int y)
    {
        TextChecks.Require(x >= 0 && x < Width && y >= 0 && y < Height, "Analytic sample outside render target.");
        return Channel(pixels, ((y * (int)Width) + x) * 8);
    }

    private static float Channel(byte[] pixels, int offset) => (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(pixels.AsSpan(offset, 2)));
}
