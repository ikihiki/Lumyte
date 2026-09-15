using System.Buffers.Binary;
using System.Numerics;

using SkiaSharp;

namespace Lumyte.Graphics.RenderGraph.Conformance;

/// <summary>Compares linear premultiplied pixels. Interior and AA boundary errors have separate limits.</summary>
public static class TwoDPixelComparison
{
    public static void AssertMatches(string name, ReadOnlySpan<byte> actual, int rowPitch, GpuFormat format,
        ReadOnlySpan<byte> reference, int size = TwoDRenderConsumer.Size, GpuFormat referenceFormat = GpuFormat.Rgba8Unorm)
    {
        float maximum = 0, sum = 0;
        var differences = new List<string>();
        byte[] packedActual = new byte[size * size * 4];
        byte[] packedReference = new byte[size * size * 4];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                var expected = Read(reference, (y * size + x) * BytesPerPixel(referenceFormat), referenceFormat);
                var observed = Read(actual, y * rowPitch + x * BytesPerPixel(format), format);
                float error = Maximum(Vector4.Abs(expected - observed));
                bool interior = IsSmoothInterior(reference, x, y, size, referenceFormat);
                // 4 quantization steps inside a shape; coverage may differ by at most 1/8 at its edge.
                float tolerance = interior ? 4f / 255 : 32f / 255;
                if (error > tolerance && differences.Count < 12)
                { differences.Add($"({x},{y}) {(interior ? "interior" : "edge")}: expected {expected}, actual {observed}, error {error:F5}"); }
                maximum = Math.Max(maximum, error);
                sum += (Vector4.Abs(expected - observed).X + Vector4.Abs(expected - observed).Y
                    + Vector4.Abs(expected - observed).Z + Vector4.Abs(expected - observed).W) / 4;
                for (int c = 0; c < 4; c++)
                {
                    packedActual[(y * size + x) * 4 + c] = (byte)Math.Clamp((int)MathF.Round(observed[c] * 255), 0, 255);
                    packedReference[(y * size + x) * 4 + c] = (byte)Math.Clamp((int)MathF.Round(expected[c] * 255), 0, 255);
                }
            }
        }
        float mean = sum / (size * size);
        if (differences.Count != 0 || mean > 2f / 255)
        {
            string directory = Path.Combine(AppContext.BaseDirectory, "TestResults", "TwoD", name);
            Directory.CreateDirectory(directory);
            SavePng(Path.Combine(directory, "actual.png"), packedActual, size);
            SavePng(Path.Combine(directory, "skia-reference.png"), packedReference, size);
            throw new InvalidOperationException($"{name}: 2D differs from SkiaSharp (max={maximum:F5}, mean={mean:F5}). "
                + string.Join(Environment.NewLine, differences) + $"{Environment.NewLine}Images: {directory}");
        }
    }

    private static bool IsSmoothInterior(ReadOnlySpan<byte> pixels, int x, int y, int size, GpuFormat format)
    {
        Vector4 minimum = new(float.PositiveInfinity), maximum = new(float.NegativeInfinity);
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int px = Math.Clamp(x + dx, 0, size - 1), py = Math.Clamp(y + dy, 0, size - 1);
                Vector4 value = Read(pixels, (py * size + px) * BytesPerPixel(format), format);
                minimum = Vector4.Min(minimum, value);
                maximum = Vector4.Max(maximum, value);
            }
        }
        return Maximum(maximum - minimum) < 0.12f;
    }
    public static int BytesPerPixel(GpuFormat format) => format == GpuFormat.Rgba16Float ? 8 : 4;
    private static float Maximum(Vector4 value) => Math.Max(Math.Max(value.X, value.Y), Math.Max(value.Z, value.W));
    private static Vector4 Read(ReadOnlySpan<byte> bytes, int offset, GpuFormat format)
    {
        if (format == GpuFormat.Rgba16Float)
        {
            return new(HalfAt(bytes, offset), HalfAt(bytes, offset + 2), HalfAt(bytes, offset + 4), HalfAt(bytes, offset + 6));
        }
        var color = new Vector4(bytes[offset], bytes[offset + 1], bytes[offset + 2], bytes[offset + 3]) / 255;
        return format == GpuFormat.Bgra8Unorm ? new(color.Z, color.Y, color.X, color.W) : color;
    }
    private static float HalfAt(ReadOnlySpan<byte> bytes, int offset)
        => (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(bytes[offset..]));
    public static void SavePng(string path, byte[] rgba, int size)
    {
        using var colorSpace = SKColorSpace.CreateSrgbLinear();
        using var bitmap = new SKBitmap(new SKImageInfo(size, size, SKColorType.Rgba8888, SKAlphaType.Premul, colorSpace));
        System.Runtime.InteropServices.Marshal.Copy(rgba, 0, bitmap.GetPixels(), rgba.Length);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = File.Create(path);
        data.SaveTo(stream);
    }
}
