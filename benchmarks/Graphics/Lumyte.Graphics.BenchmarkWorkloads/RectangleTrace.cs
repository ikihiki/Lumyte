using System.Security.Cryptography;
using System.Text;
using Lumyte.Graphics.Drawing2D;

namespace Lumyte.Graphics.BenchmarkWorkloads;

/// <summary>Versioned, immutable logical input, independent of renderer and partition.</summary>
public sealed class RectangleTrace
{
    /// <summary>The fixed number of visible logical rectangles.</summary>
    public const int Count = 4096;

    /// <summary>The trace generation version.</summary>
    public const string Version = "rectangles-v2";

    /// <summary>Fixed logical frames between full visible burst transitions.</summary>
    public const int BurstPeriod = 120;

    /// <summary>First burst frame; frame 150 returns to the initial image.</summary>
    public const int FirstBurstFrame = 30;

    private readonly bool[] _selected = new bool[Count];

    /// <summary>Initializes a new instance of the <see cref="RectangleTrace"/> class.</summary>
    /// <param name="update">The logical update profile.</param>
    /// <param name="locality">The contiguous or dispersed selection rule.</param>
    /// <param name="seed">The fixed permutation seed.</param>
    public RectangleTrace(string update, string locality, uint seed = 27)
    {
        if (locality is not ("contiguous" or "dispersed"))
        {
            throw new ArgumentException("Unknown locality.", nameof(locality));
        }

        ChangedCount = update switch
        {
            "static" => 0,
            "one" => 1,
            "1%" => 41,
            "10%" => 410,
            "all" or "burst" => Count,
            _ => throw new ArgumentException("Unknown update.", nameof(update)),
        };
        Update = update;
        Locality = locality;
        Seed = seed;

        // Odd affine permutation of 2^12: unique, seed-fixed and spread across pages.
        int start = (int)(seed & 4095);
        for (int i = 0; i < ChangedCount; i++)
        {
            int index = locality == "contiguous" ? (start + i) % Count : (start + (i * 2053)) % Count;
            _selected[index] = true;
        }
    }

    /// <summary>Gets the update profile.</summary>
    public string Update { get; }

    /// <summary>Gets the spatial selection rule.</summary>
    public string Locality { get; }

    /// <summary>Gets the seed.</summary>
    public uint Seed { get; }

    /// <summary>Gets the rounded selection size; the fixture reports zero actual changes on static intervals.</summary>
    public int ChangedCount { get; }

    /// <summary>Validates the fixed granularity sweep.</summary>
    /// <param name="granularity">The number of commands per content node.</param>
    public static void ValidateGranularity(int granularity)
    {
        if (granularity is not (1 or 16 or 64 or 256 or 4096))
        {
            throw new ArgumentOutOfRangeException(nameof(granularity));
        }
    }

    /// <summary>Gets whether the logical element changes on each transition.</summary>
    /// <param name="index">The logical element index.</param>
    /// <returns>The deterministic result for the requested input.</returns>
    public bool Selected(int index) => _selected[index];

    /// <summary>Gets binary color state without accumulated mutable history.</summary>
    /// <param name="index">The logical element index.</param>
    /// <param name="frame">The logical frame number.</param>
    /// <returns>The deterministic result for the requested input.</returns>
    public bool IsWhite(int index, int frame) => _selected[index] && State(frame);

    /// <summary>Gets the binary visible state at an absolute logical frame, including jumped frames.</summary>
    /// <param name="frame">Nonnegative logical frame.</param>
    /// <returns>Whether selected rectangles are white.</returns>
    public bool State(int frame)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frame);
        return Update == "burst" ? frame >= FirstBurstFrame && (((frame - FirstBurstFrame) / BurstPeriod) & 1) == 0 : (frame & 1) != 0;
    }

    /// <summary>Gets the most recent deterministic burst event frame, or null before the first event.</summary>
    /// <param name="frame">Logical frame.</param>
    /// <returns>The event frame.</returns>
    public int? BurstEventFrame(int frame) => Update == "burst" && frame >= FirstBurstFrame ? FirstBurstFrame + (((frame - FirstBurstFrame) / BurstPeriod) * BurstPeriod) : null;

    /// <summary>Gets the nearest scheduled event for signed pre/post-burst aggregation.</summary>
    /// <param name="frame">Logical frame.</param>
    /// <returns>The event anchor, or null for non-burst traces.</returns>
    public int? BurstAnchor(int frame) => Update == "burst" ? FirstBurstFrame + (Math.Max(0, (frame - FirstBurstFrame + (BurstPeriod / 2)) / BurstPeriod) * BurstPeriod) : null;

    /// <summary>Gets a partition-independent ID of the complete ordered input state.</summary>
    /// <param name="frame">The logical frame number.</param>
    /// <returns>The deterministic result for the requested input.</returns>
    public string InputId(int frame)
    {
        byte[] state = new byte[Count];
        for (int i = 0; i < Count; i++)
        {
            state[i] = IsWhite(i, frame) ? (byte)1 : (byte)0;
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Version).Concat(state).ToArray()));
    }

    /// <summary>Gets changed content partitions without duplicates.</summary>
    /// <param name="granularity">The number of commands per content node.</param>
    /// <returns>The deterministic result for the requested input.</returns>
    public int[] ChangedNodes(int granularity)
    {
        ValidateGranularity(granularity);
        return Enumerable.Range(0, Count).Where(Selected).Select(i => i / granularity).Distinct().ToArray();
    }

    /// <summary>Records an independent canonical stream, never flattening a Scene.</summary>
    /// <param name="frame">The logical frame number.</param>
    /// <param name="width">The physical target width.</param>
    /// <param name="height">The physical target height.</param>
    /// <returns>The deterministic result for the requested input.</returns>
    public DrawList2D Reference(int frame, int width = 1920, int height = 1080)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        RecordRange(canvas, frame, 0, Count, width, height);
        return canvas.End();
    }

    /// <summary>Records source input in painter order; binary colors avoid ambiguous sRGB rounding.</summary>
    /// <param name="canvas">The active recording Canvas.</param>
    /// <param name="frame">The logical frame number.</param>
    /// <param name="first">The first logical element index.</param>
    /// <param name="count">The number of logical elements to record.</param>
    /// <param name="width">The physical target width.</param>
    /// <param name="height">The physical target height.</param>
    public void RecordRange(Canvas2D canvas, int frame, int first, int count, int width, int height)
    {
        int cellWidth = width / 64;
        int cellHeight = height / 64;
        for (int i = first; i < first + count; i++)
        {
            Color2D color = IsWhite(i, frame) ? new(1, 1, 1, 1) : new(1, 0, 0, 1);
            canvas.FillRect(new((i % 64) * cellWidth, (i / 64) * cellHeight, cellWidth - 2, cellHeight - 2), color);
        }
    }

    /// <summary>Checks analytic interior colors for every element, independently of Canvas/Scene equivalence.</summary>
    /// <param name="pixels">The tightly packed RGBA8 readback.</param>
    /// <param name="frame">The logical frame number.</param>
    /// <param name="width">The physical target width.</param>
    /// <param name="height">The physical target height.</param>
    public void VerifyAnalytic(byte[] pixels, int frame, int width, int height)
    {
        if (pixels.Length != width * height * 4)
        {
            throw new InvalidOperationException("Invalid RGBA8 readback length.");
        }

        for (int i = 0; i < Count; i++)
        {
            int x = ((i % 64) * (width / 64)) + 2;
            int y = ((i / 64) * (height / 64)) + 2;
            int offset = ((y * width) + x) * 4;
            byte secondary = IsWhite(i, frame) ? (byte)255 : (byte)0;
            if (pixels[offset] != 255 || pixels[offset + 1] != secondary || pixels[offset + 2] != secondary || pixels[offset + 3] != 255)
            {
                throw new InvalidOperationException($"Analytic color mismatch at logical element {i}.");
            }
        }
    }
}
