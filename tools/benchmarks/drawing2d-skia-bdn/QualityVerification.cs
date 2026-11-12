using System.Security.Cryptography;

internal static class QualityVerification
{
    internal static async Task RunAsync(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        using var report = new StreamWriter(Path.Combine(outputDirectory, "quality.txt"));
        foreach (string name in new[] { "rect-static", "rect-dynamic", "path-static", "path-dynamic", "glyph-static", "glyph-dynamic" })
        {
            var trace = new Trace(name);
            using var skia = new SkiaAdapter(trace, aa: true);
            using var lumyte = new LumyteAdapter(trace);
            await report.WriteLineAsync($"{name}: Skia={skia.Identity}; Lumyte={lumyte.Identity}");
            for (int state = 0; state < 32; state++)
            {
                skia.Record(state); lumyte.Record(state);
                await skia.RenderAsync(); await lumyte.RenderAsync();
                byte[] a = skia.Pixels(), b = lumyte.Pixels();
                string stem = $"{name}-state{state:D2}";
                await File.WriteAllBytesAsync(Path.Combine(outputDirectory, stem + "-skia.rgba"), a);
                await File.WriteAllBytesAsync(Path.Combine(outputDirectory, stem + "-lumyte.rgba"), b);
                if (name.StartsWith("rect", StringComparison.Ordinal))
                {
                    byte[] expected = RectangleOracle(trace.Static ? 0 : state);
                    if (!a.AsSpan().SequenceEqual(expected) || !b.AsSpan().SequenceEqual(expected))
                        throw new InvalidOperationException(stem + ": independent exact RGBA rectangle oracle failed.");
                }
                if (a.Length != Trace.W * Trace.H * 4 || b.Length != a.Length)
                    throw new InvalidOperationException("Unexpected readback size.");
                long absoluteError = 0; int maxError = 0, differentPixels = 0;
                for (int pixel = 0; pixel < a.Length; pixel += 4)
                {
                    bool differs = false;
                    for (int c = 0; c < 4; c++)
                    {
                        int error = Math.Abs(a[pixel+c] - b[pixel+c]);
                        absoluteError += error; maxError = Math.Max(maxError, error); differs |= error != 0;
                    }
                    if (differs) differentPixels++;
                }
                await report.WriteLineAsync($"{stem}: skia={Convert.ToHexString(SHA256.HashData(a))}; lumyte={Convert.ToHexString(SHA256.HashData(b))}; differentPixels={differentPixels}; maxChannelError={maxError}; meanAbsoluteChannelError={(double)absoluteError/a.Length:R}; " +
                    (name.StartsWith("rect", StringComparison.Ordinal) ? "exact independent RGBA PASS" : "AA/color-space QUALIFICATION REQUIRED; not a pixel-equivalence pass"));
                await report.FlushAsync();
            }
        }
        Console.WriteLine("Rectangle oracles passed. Complex fixtures require reviewing quality.txt and raw images; no complex pixel-equivalence claim.");
    }

    private static byte[] RectangleOracle(int state)
    {
        byte[] result = new byte[Trace.W * Trace.H * 4];
        for (int i = 0; i < 1000; i++)
        {
            int x = 12 + i % 50 * 37 + state % 4, y = 12 + i / 50 * 48;
            for (int yy = y; yy < y + 28; yy++)
                for (int xx = x; xx < x + 30; xx++)
                {
                    int offset = (yy * Trace.W + xx) * 4;
                    result[offset + i % 3] = 255;
                    result[offset + 3] = 255;
                }
        }
        return result;
    }
}
