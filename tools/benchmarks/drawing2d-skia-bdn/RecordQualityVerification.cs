using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

static class RecordQualityVerification
{
    public static async Task RunAsync(string directory, bool write)
    {
        Directory.CreateDirectory(directory);
        List<object> rows = [];
        foreach (string name in new[] { "rect1000-dynamic", "rect1000-cached", "rect341-dynamic", "rect342-dynamic", "path200-dynamic", "path200-cached", "mixed200-dynamic", "mixed200-cached" })
        {
            var fixture = new RecordFrameBenchmarks { Case = name };
            await fixture.Setup();
            try
            {
                for (int state = 0; state < 32; state++)
                {
                    await fixture.CompletedFrameAsync();
                    byte[] actual = await fixture.PixelsAsync();
                    string hash = Convert.ToHexString(SHA256.HashData(actual));
                    string path = Path.Combine(directory, name + "-" + state + ".rgba.gz");
                    long differences = 0; int maximum = 0;
                    if (write)
                    {
                        using var file = File.Create(path);
                        using var gzip = new GZipStream(file, CompressionLevel.Fastest);
                        await gzip.WriteAsync(actual);
                    }
                    else
                    {
                        using var file = File.OpenRead(path);
                        using var gzip = new GZipStream(file, CompressionMode.Decompress);
                        using var bytes = new MemoryStream();
                        await gzip.CopyToAsync(bytes);
                        byte[] expected = bytes.ToArray();
                        if (expected.Length != actual.Length) throw new InvalidOperationException("Invalid baseline image extent.");
                        for (int i = 0; i < actual.Length; i++)
                        {
                            int difference = Math.Abs(actual[i] - expected[i]);
                            if (difference != 0) differences++;
                            maximum = Math.Max(maximum, difference);
                        }
                    }
                    bool analytic = !name.StartsWith("rect") || RectangleOracle(actual, name, fixture.LastRecordedState);
                    if (!analytic) throw new InvalidOperationException("Independent integer rectangle oracle failed: " + name);
                    rows.Add(new { Case = name, sequence = state, logicalState = fixture.LastRecordedState, hash, differingChannels = differences, maximumChannelDifference = maximum, independentRectangleOracle = name.StartsWith("rect") ? "exact-pass" : "not-applicable", width = 1920, height = 1080 });
                }
            }
            finally { fixture.Cleanup(); }
        }
        await File.WriteAllTextAsync(Path.Combine(directory, write ? "baseline.json" : "candidate.json"), JsonSerializer.Serialize(rows));
    }

    static bool RectangleOracle(byte[] actual, string name, int state)
    {
        int count = name.StartsWith("rect341") ? 341 : name.StartsWith("rect342") ? 342 : 1000;
        byte[] expected = new byte[actual.Length];
        int shift = state & 3;
        for (int i = 0; i < count; i++)
        {
            int left = 12 + (i % 50) * 37 + shift, top = 12 + (i / 50) * 48;
            for (int y = top; y < top + 28; y++) for (int x = left; x < left + 30; x++)
            {
                int p = (y * 1920 + x) * 4; expected[p + i % 3] = 255; expected[p + 3] = 255;
            }
        }
        return actual.AsSpan().SequenceEqual(expected);
    }
}
