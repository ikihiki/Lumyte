using System.Numerics;
using BenchmarkDotNet.Attributes;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Benchmarks.Testing;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Mathematics.Colors;

namespace Lumyte.Graphics.Text.Benchmark;

/// <summary>Independent vector snapshots, real CPU distance fields and managed atlas staging.</summary>
[MemoryDiagnoser]
[BenchmarkCategory("Screening", "Comparison", "Diagnostic")]
public class TextSnapshotBenchmarks
{
    private TextLayout2D _layout = null!;
    private Path2D _path = null!;
    private CpuGraphicsDevice _device = null!;

    /// <summary>Gets or sets the bounded outline count.</summary>
    [Params(1, 16)]
    public int Outlines { get; set; }

    /// <summary>Creates reusable vector inputs outside the measurement.</summary>
    [GlobalSetup]
    public void Setup()
    {
        var path = new PathBuilder2D();
        path.MoveTo(Vector2.Zero);
        path.QuadraticTo(new Vector2(12, -2), new Vector2(12, 12));
        path.LineTo(new Vector2(0, 12));
        path.Close();
        _path = path.Build();
        var paint = PaintGraph2D.Solid(new SRgba<float>(0.1f, 0.2f, 0.3f, 1));
        _layout = TextLayout2D.FromDrawings("vector snapshot", new Rect2D(0, -2, 12, 14), new Rect2D(0, 0, 16, 20), 1, Enumerable.Range(0, Outlines).Select(_ => new TextGeometry2D(_path, paint)));
        _device = new CpuGraphicsDevice();
    }

    /// <summary>Copies immutable drawing metadata, retaining immutable outlines.</summary>
    /// <returns>The independent snapshot.</returns>
    [Benchmark]
    public TextLayout2D Snapshot() => TextLayout2D.FromDrawings(_layout.Text, _layout.Bounds, _layout.AdvanceBounds, _layout.LineCount, _layout.Drawings);

    /// <summary>Runs the actual multichannel signed-distance generator.</summary>
    /// <returns>The generated pixels.</returns>
    public DistanceFieldImage2D GenerateMsdf() => DistanceFieldGenerator2D.Generate(_path, 1, 4, DistanceFieldMode2D.Msdf);

    /// <summary>Runs the actual four-channel distance generator.</summary>
    /// <returns>The generated pixels.</returns>
    public DistanceFieldImage2D GenerateMtsdf() => DistanceFieldGenerator2D.Generate(_path, 1, 4, DistanceFieldMode2D.Mtsdf);

    /// <summary>Generates, packs, stages and records atlas upload without GPU execution.</summary>
    /// <returns>The bounded device observations.</returns>
    [Benchmark]
    public async Task<CpuGraphicsCounters> StageAtlasAsync()
    {
        _device.ResetCounters();
        using DistanceFieldText2D atlas = await DistanceFieldText2D.CreateAsync(_device, _layout, distanceRange: 4);
        using IGraphicsCommandBuffer commands = _device.CreateCommandBuffer(new CommandBufferDesc());
        atlas.RecordUpload(commands);
        commands.Finish();
        return _device.Counters;
    }

    /// <summary>Releases the managed device.</summary>
    [GlobalCleanup]
    public void Cleanup() => _device.Dispose();
}
