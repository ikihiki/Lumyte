using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Benchmarks.Testing;
using Lumyte.Mathematics.Colors;

namespace Lumyte.Graphics.Drawing2D.Benchmark.Primitives;

/// <summary>Direct primitive preparation on the real default renderer with caller-owned managed sampled resources.</summary>
public sealed class PrimitiveCpuFixture : IDisposable
{
    private readonly IGraphicsTexture _targetTexture;
    private readonly IGraphicsTextureView _targetView;
    private readonly IGraphicsTexture _colorTexture;
    private readonly IGraphicsTextureView _colorView;
    private readonly IGraphicsTexture _coverageTexture;
    private readonly IGraphicsTextureView _coverageView;
    private readonly IGraphicsSampler _sampler;
    private readonly Renderer2D _renderer;
    private readonly RenderTarget2D _target;
    private readonly DrawList2D _drawing;

    /// <summary>Initializes a new instance of the <see cref="PrimitiveCpuFixture"/> class and uploads fixed sampled inputs outside timing.</summary>
    /// <param name="kind">The public primitive API to record.</param>
    public PrimitiveCpuFixture(PrimitiveKind kind)
    {
        Device = new();
        _targetTexture = Device.CreateTexture(new() { Width = 512, Height = 512, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment });
        _targetView = _targetTexture.CreateView();
        _target = new() { View = _targetView };
        _colorTexture = Sampled(TextureFormat.Rgba8Unorm);
        _colorView = _colorTexture.CreateView();
        _coverageTexture = Sampled(TextureFormat.R8Unorm);
        _coverageView = _coverageTexture.CreateView();
        _sampler = Device.CreateSampler(new());
        _renderer = new(Device, DrawingCommandRenderers2D.CreateDefault());
        _drawing = Build(kind);
    }

    /// <summary>Gets the synthetic CPU recording device.</summary>
    public CpuGraphicsDevice Device { get; }

    /// <summary>Gets the actual number of recorded public commands.</summary>
    public int CommandCount => _drawing.Count;

    /// <summary>Prepares and records the fixed immutable command list on a warm renderer, without shader execution.</summary>
    /// <returns>The consumed upload and command checksum.</returns>
    public ulong PrepareRecordDispose()
    {
        Device.ResetCounters();
        using PreparedDraw2D prepared = _renderer.PrepareAsync(_drawing, _target).GetAwaiter().GetResult();
        using IGraphicsCommandBuffer commands = Device.CreateCommandBuffer(new());
        prepared.Record(commands);
        commands.Finish();
        return Device.Counters.Checksum;
    }

    /// <summary>Releases renderer-owned allocations before borrowed sampled inputs and device.</summary>
    public void Dispose()
    {
        _renderer.Dispose();
        _sampler.Dispose();
        _coverageView.Dispose();
        _coverageTexture.Dispose();
        _colorView.Dispose();
        _colorTexture.Dispose();
        _targetView.Dispose();
        _targetTexture.Dispose();
        Device.Dispose();
    }

    private IGraphicsTexture Sampled(TextureFormat format)
    {
        IGraphicsTexture texture = Device.CreateTexture(new() { Width = 4, Height = 4, Format = format, Usage = TextureUsage.Sampled | TextureUsage.CopyDestination });
        uint rowBytes = format == TextureFormat.R8Unorm ? 4U : 16U;
        byte[] data = new byte[rowBytes * 4];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)((i * 17) % 256);
        }

        using IGraphicsBuffer<byte> upload = Device.CreateBuffer<byte>(new() { Count = (ulong)data.Length, Memory = MemoryPreference.Upload, Usage = BufferUsage.CopySource });
        upload.MapAsync().GetAwaiter().GetResult();
        upload.CopyBytesFrom(data, 0, (ulong)data.Length);
        upload.Unmap();
        using IGraphicsCommandBuffer commands = Device.CreateCommandBuffer(new());
        commands.CopyBufferToTexture(new() { Buffer = upload.SliceElements(0, (ulong)data.Length), BytesPerRow = rowBytes, RowsPerImage = 4 }, new() { Texture = texture, Width = 4, Height = 4 });
        commands.Finish();
        using IGraphicsSubmission submission = Device.Queue.Submit([commands]);
        submission.WaitAsync().GetAwaiter().GetResult();
        return texture;
    }

    private DrawList2D Build(PrimitiveKind kind)
    {
        var image = new Image2D(_colorView, _sampler);
        var coverage = new CoverageImage2D(_coverageView, _sampler);
        var distance = new DistanceField2D(_coverageView, _sampler, DistanceFieldEncoding2D.Sdf, 4);
        var color = new SRgba<float>(0.2f, 0.5f, 0.8f, 1);
        var paint = PaintGraph2D.Solid(color);
        var stroke = new StrokeStyle2D { Width = 2, Cap = LineCap2D.Round, Join = LineJoin2D.Round };
        var path = new PathBuilder2D();
        path.MoveTo(new(0, 0));
        path.CubicTo(new(12, 0), new(0, 12), new(12, 12));
        Path2D curve = path.Build();
        var mesh = new Mesh2D([new(new(0, 0), 1), new(new(16, 0), 1), new(new(0, 16), 0)], [0, 1, 2]);
        var canvas = new Canvas2D();
        canvas.Begin();
        for (int i = 0; i < 64; i++)
        {
            canvas.Save();
            canvas.Translate(new((i % 8) * 24, (i / 8) * 24));
            switch (kind)
            {
                case PrimitiveKind.RoundedRectangle:
                    canvas.FillRoundedRect(new(0, 0, 16, 16), 3, color);
                    break;
                case PrimitiveKind.Circle:
                    canvas.FillCircle(new(8, 8), 8, color);
                    break;
                case PrimitiveKind.Line:
                    canvas.DrawLine(new(0, 0), new(16, 16), color, stroke);
                    break;
                case PrimitiveKind.StrokedPath:
                    canvas.StrokePath(curve, color, stroke);
                    break;
                case PrimitiveKind.Image:
                    canvas.DrawImage(image, new(0, 0, 4, 4), new(0, 0, 16, 16));
                    break;
                case PrimitiveKind.CoverageMask:
                    canvas.DrawCoverageMask(coverage, new(0, 0, 4, 4), new(0, 0, 16, 16), paint);
                    break;
                case PrimitiveKind.DistanceField:
                    canvas.DrawDistanceField(distance, new(0, 0, 4, 4), new(0, 0, 16, 16), paint);
                    break;
                case PrimitiveKind.Mesh:
                    canvas.DrawMesh(mesh, paint);
                    break;
                case PrimitiveKind.PaintGraph:
                    canvas.DrawPaintGraph(paint);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind));
            }

            canvas.Restore();
        }

        return canvas.End();
    }
}
