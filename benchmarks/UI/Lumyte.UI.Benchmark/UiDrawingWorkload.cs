using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Benchmarks.Testing;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Mathematics.Colors;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Benchmark;

/// <summary>Public UI retained drawing and managed simulated submission, not GPU execution.</summary>
public sealed class UiDrawingWorkload : IDisposable
{
    private readonly Compose.Definitions.Rectangle[] _leaves;
    private readonly Renderer2D _renderer;
    private readonly IGraphicsTexture _texture;
    private readonly IGraphicsTextureView _view;
    private readonly bool[] _alternate;

    /// <summary>Initializes a new instance of the <see cref="UiDrawingWorkload"/> class.</summary>
    /// <param name="count">Rendered rectangles.</param>
    public UiDrawingWorkload(int count)
    {
        _alternate = new bool[count];
        _leaves = Enumerable.Range(0, count).Select(_ => Rectangle(width: 8, height: 4, fill: new SRgba<float>(1, 0, 0, 1))).ToArray();
        Surface = new UiSurface(Stack()[_leaves]);
        Surface.SetViewport(new(512, 2048));
        Drawing = Surface.CreateDrawingScene();
        Drawing.Update();
        Device = new CpuGraphicsDevice();
        _renderer = new(Device, DrawingCommandRenderers2D.CreateDefault(), retentionOptions: new() { MaxFramesInFlight = 1, MaxCachedAssets = count + 1 });
        _texture = Device.CreateTexture(new() { Width = 512, Height = 2048, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment });
        _view = _texture.CreateView();
    }

    /// <summary>Gets the public surface.</summary>
    public UiSurface Surface { get; }

    /// <summary>Gets the CPU local recording adapter.</summary>
    public UiDrawingScene2D Drawing { get; }

    /// <summary>Gets managed observations, never GPU counters.</summary>
    public CpuGraphicsDevice Device { get; }

    /// <summary>Gets renderer cache observations.</summary>
    public DrawingRetentionStatistics2D Retention => _renderer.RetentionStatistics;

    /// <summary>Changes one or every actual fill before updating retained content.</summary>
    /// <param name="all">Whether every rectangle changes.</param>
    public void Update(bool all)
    {
        Mutate(all);
        Drawing.Update();
    }

    /// <summary>Updates one fill and explicitly freezes a snapshot placement plan.</summary>
    /// <returns>The frozen revision.</returns>
    public ulong Snapshot()
    {
        Update(false);
        using DrawingSnapshot2D snapshot = Drawing.Scene.CaptureSnapshot();
        return snapshot.Revision;
    }

    /// <summary>Records public UI commands through the standard renderer and a real simulated submission owner.</summary>
    /// <returns>Completion of CPU simulation and owner retirement.</returns>
    public async ValueTask StandardSimulatedAsync()
    {
        Mutate(false);
        Surface.UpdateLayout();
        var canvas = new Canvas2D();
        canvas.Begin();
        Surface.Record(canvas);
        using PreparedDraw2D prepared = await _renderer.PrepareAsync(canvas.End(), new() { View = _view }).ConfigureAwait(false);
        using IGraphicsCommandBuffer commands = Device.CreateCommandBuffer(new());
        prepared.Record(commands);
        commands.Finish();
        using IGraphicsSubmission submission = Device.Queue.Submit([commands]);
        await submission.WaitAsync().ConfigureAwait(false);
    }

    /// <summary>Updates, borrows, prepares, releases the borrow, then records and retires an attached simulated owner.</summary>
    /// <returns>Completion of CPU simulation; no GPU work.</returns>
    public async ValueTask RetainedSimulatedAsync()
    {
        Update(false);
        PreparedDrawingFrame2D frame;
        using (DrawingSceneReadLease2D borrow = Drawing.Scene.Borrow())
        {
            frame = await _renderer.PrepareAsync(borrow, new() { View = _view }).ConfigureAwait(false);
        }

        using (frame)
        using (IGraphicsCommandBuffer commands = Device.CreateCommandBuffer(new()))
        {
            frame.Record(commands);
            commands.Finish();
            using IGraphicsSubmission submission = Device.Queue.Submit([commands]);
            frame.AttachSubmission(submission);
            await submission.WaitAsync().ConfigureAwait(false);
            commands.Dispose();
            frame.Retire();
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _renderer.Dispose();
        _view.Dispose();
        _texture.Dispose();
        Device.Dispose();
        Drawing.Dispose();
        Surface.Dispose();
    }

    private void Mutate(bool all)
    {
        int count = all ? _leaves.Length : 1;
        for (int i = 0; i < count; i++)
        {
            _alternate[i] = !_alternate[i];
            _leaves[i].Fill = _alternate[i] ? new(0, 1, 0, 1) : new(1, 0, 0, 1);
        }
    }
}
