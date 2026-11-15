using System.Numerics;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Benchmarks.Testing;
using Lumyte.Mathematics.Colors;

namespace Lumyte.Graphics.Drawing2D.Benchmark.Retained;

/// <summary>Real public scene, renderer and completion-safe retained frames on a managed recording device.</summary>
public sealed class RetainedSceneFixture : IDisposable
{
    private readonly DrawList2D _first = Content(0);
    private readonly DrawList2D _second = Content(2.1f);
    private readonly Path2D _clip;
    private readonly DrawingNodeId2D _node;
    private readonly int _count;
    private readonly IGraphicsTexture _texture;
    private readonly IGraphicsTextureView _view;
    private readonly Renderer2D _renderer;
    private readonly RenderTarget2D _target;
    private DrawingNodeId2D _extra;
    private bool _alternate;

    /// <summary>Initializes a new instance of the <see cref="RetainedSceneFixture"/> class with individually transformed shared placements.</summary>
    /// <param name="count">The fixed base placement count.</param>
    public RetainedSceneFixture(int count)
    {
        _count = count;
        Device = new();
        Scene = new();
        var path = new PathBuilder2D();
        path.MoveTo(new(0, 0));
        path.LineTo(new(16, 0));
        path.LineTo(new(16, 16));
        path.LineTo(new(0, 16));
        path.Close();
        _clip = path.Build();
        for (int i = 0; i < count; i++)
        {
            DrawingNodeId2D node = Scene.Insert(Scene.Root, i, _first);
            Scene.SetTransform(node, Matrix3x2.CreateTranslation((i % 32) * 12, (i / 32) * 12));
            if (i == 0)
            {
                _node = node;
            }
        }

        _texture = Device.CreateTexture(new() { Width = 512, Height = 512, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment });
        _view = _texture.CreateView();
        _target = new() { View = _view };
        _renderer = new(Device, DrawingCommandRenderers2D.CreateDefault(), retentionOptions: new() { MaxCachedAssets = count, MaxCachedBytes = 2L * 1024 * 1024 * 1024 });
    }

    /// <summary>Gets the CPU-only device and its operation observations.</summary>
    public CpuGraphicsDevice Device { get; }

    /// <summary>Gets the caller-owned public scene.</summary>
    public DrawingScene2D Scene { get; }

    /// <summary>Gets exact public retained-cache observations.</summary>
    public DrawingRetentionStatistics2D Statistics => _renderer.RetentionStatistics;

    /// <summary>Alternates a real edit and publishes its placement plan, separately from renderer work.</summary>
    /// <param name="mutation">The edit to alternate.</param>
    /// <returns>The changed public scene revision.</returns>
    public ulong EditAndPublish(SceneMutation mutation)
    {
        _alternate = !_alternate;
        switch (mutation)
        {
            case SceneMutation.Content:
                Scene.ReplaceContent(_node, _alternate ? _second : _first);
                break;
            case SceneMutation.Transform:
                Scene.SetTransform(_node, Matrix3x2.CreateTranslation(_alternate ? 1 : 0, 0));
                break;
            case SceneMutation.Clip:
                Scene.SetClip(_node, _alternate ? _clip : null);
                break;
            case SceneMutation.InsertRemove:
                if (_alternate)
                {
                    _extra = Scene.Insert(Scene.Root, _count, _second);
                }
                else
                {
                    Scene.Remove(_extra);
                }

                break;
            case SceneMutation.Reorder:
                Scene.Move(_node, Scene.Root, _alternate ? _count - 1 : 0);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mutation));
        }

        using DrawingSceneReadLease2D published = Scene.Borrow();
        return published.Revision;
    }

    /// <summary>Prepares, records, CPU-submits, explicitly waits for simulated completion and retires, without scene edits.</summary>
    /// <param name="snapshot">Use an explicit metadata snapshot instead of a live borrow.</param>
    /// <returns>The checksum of actual command consumption.</returns>
    public ulong PrepareRecordRetire(bool snapshot = false)
    {
        Device.ResetCounters();
        PreparedDrawingFrame2D frame;
        if (snapshot)
        {
            using DrawingSnapshot2D source = Scene.CaptureSnapshot();
            frame = _renderer.PrepareAsync(source, _target).GetAwaiter().GetResult();
        }
        else
        {
            using DrawingSceneReadLease2D source = Scene.Borrow();
            frame = _renderer.PrepareAsync(source, _target).GetAwaiter().GetResult();
        }

        using (frame)
        {
            using IGraphicsCommandBuffer commands = Device.CreateCommandBuffer(new());
            frame.Record(commands);
            commands.Finish();
            using IGraphicsSubmission submission = Device.Queue.Submit([commands]);
            frame.AttachSubmission(submission);
            submission.WaitAsync().GetAwaiter().GetResult();
            commands.Dispose();
            frame.Retire();
        }

        return Device.Counters.Checksum;
    }

    /// <summary>Releases all retired cached resources, then target resources and the device.</summary>
    public void Dispose()
    {
        _renderer.Dispose();
        Scene.Dispose();
        _view.Dispose();
        _texture.Dispose();
        Device.Dispose();
    }

    private static DrawList2D Content(float offset)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        canvas.Rotate(offset * 0.1f);
        canvas.FillRect(new(offset, 0, 10, 10), new SRgba<float>(0.2f, 0.5f, 0.8f, 1));
        return canvas.End();
    }
}
