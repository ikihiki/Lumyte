using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Wgpu;
using Lumyte.UI.Application.Desktop.Benchmark.HostCompletedFrame;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Application.Desktop.Benchmark.BackendCpu;

/// <summary>Records actual UI-produced retained drawing on real wgpu; CPU probes never submit or wait for GPU execution.</summary>
public sealed class NativeUiRecordingFixture : IDisposable
{
    private readonly UiSurface _surface;
    private readonly UiDrawingScene2D _scene;
    private readonly DrawingSnapshot2D _snapshot;
    private WgpuDevice _device = null!;
    private Renderer2D _warm = null!;
    private IGraphicsTexture _texture = null!;
    private IGraphicsTextureView _view = null!;
    private RenderTarget2D _target = null!;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="NativeUiRecordingFixture"/> class with exactly 100 rectangles, without GPU initialization.</summary>
    public NativeUiRecordingFixture()
    {
        Definitions.Stack root = Stack();
        var rows = new List<Definitions.Widget>();
        for (int rowIndex = 0; rowIndex < 10; rowIndex++)
        {
            Definitions.Stack row = Stack(orientation: UiOrientation.Horizontal, height: 8);
            row.Children.Initialize(Enumerable.Range(rowIndex * 10, 10).Select(index => Rectangle(width: 8, height: 8, fill: index % 2 == 0 ? new Lumyte.Mathematics.Colors.SRgba<float>(1, 0, 0, 1) : new(0, 1, 0, 1))).ToArray());
            rows.Add(row);
        }

        root.Children.Initialize(rows.ToArray());
        _surface = new(root);
        _surface.SetViewport(new(128, 128));
        _scene = _surface.CreateDrawingScene();
        _scene.Update();
        _snapshot = _scene.Scene.CaptureSnapshot();
    }

    /// <summary>Gets the actual native selected adapter line, not a separate probe.</summary>
    public string? AdapterIdentity { get; private set; }

    /// <summary>Gets explicit submissions made only by untimed pixel validation.</summary>
    public int ValidationSubmissions { get; private set; }

    /// <summary>Gets the real renderer's retained ownership and reuse counters.</summary>
    public DrawingRetentionStatistics2D WarmStatistics => _warm.RetentionStatistics;

    /// <summary>Gets the missing opt-in prerequisite without initializing graphics; no display is required for offscreen recording.</summary>
    /// <returns>A real skip reason, or null when explicit GPU initialization was authorized.</returns>
    public static string? UnavailableReason() => Environment.GetEnvironmentVariable("LUMYTE_UI_BENCHMARK_GPU") == "1" ? null : "Real wgpu backend CPU verification requires LUMYTE_UI_BENCHMARK_GPU=1.";

    /// <summary>Creates the actual hardware device and validates equivalent warm/cold renderer pixels outside timing.</summary>
    /// <returns>The untimed initialization, actual completion waits and analytic readback validation.</returns>
    public async Task StartAsync()
    {
        if (UnavailableReason() is { } reason)
        {
            throw new InvalidOperationException(reason);
        }

        using (var audit = new NativeAdapterAudit())
        {
            _device = WgpuDevice.Create();
            AdapterIdentity = audit.VerifyHardware();
        }

        _texture = _device.CreateTexture(new() { Width = 128, Height = 128, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
        _view = _texture.CreateView();
        _target = new() { View = _view, PixelScale = 1, LoadOp = AttachmentLoadOp.Clear, ClearColor = new(1, 1, 1, 1) };
        _warm = CreateRenderer();
        await ValidatePixelsAsync();
    }

    /// <summary>Prepares, records, finishes and discards on the resident renderer; includes CPU resource cleanup but no Submit/Wait.</summary>
    /// <returns>The disposed, explicitly never-submitted native command-buffer state.</returns>
    public Task<CommandBufferState> WarmRecordAsync() => RecordAndDiscardAsync(_warm);

    /// <summary>Includes a fresh renderer's creation, preparation, recording, finish, explicit abort and disposal; reuses the device and UI snapshot.</summary>
    /// <returns>The disposed, explicitly never-submitted native command-buffer state.</returns>
    public async Task<CommandBufferState> ColdRendererRecordAsync()
    {
        using Renderer2D renderer = CreateRenderer();
        return await RecordAndDiscardAsync(renderer);
    }

    /// <summary>Untimed real Submit/Wait/readback for the equivalent warm and fresh-renderer paths, checked against every analytic pixel.</summary>
    /// <returns>Actual hardware validation, not benchmark work or presentation evidence.</returns>
    public async Task ValidatePixelsAsync()
    {
        byte[] warm = await RenderPixelsAsync(_warm);
        using Renderer2D cold = CreateRenderer();
        byte[] fresh = await RenderPixelsAsync(cold);
        VerifyAnalyticPixels(warm);
        VerifyAnalyticPixels(fresh);
        if (!warm.AsSpan().SequenceEqual(fresh))
        {
            throw new InvalidDataException("Warm and fresh UI renderer outputs differ.");
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (!_disposed)
        {
            _warm?.Dispose();
            _view?.Dispose();
            _texture?.Dispose();
            _device?.Dispose();
            _snapshot.Dispose();
            _scene.Dispose();
            _surface.Dispose();
            _disposed = true;
        }
    }

    private static void VerifyAnalyticPixels(byte[] pixels)
    {
        if (pixels.Length != 128 * 128 * 4)
        {
            throw new InvalidDataException("Unexpected real GPU readback size.");
        }

        for (int y = 0; y < 128; y++)
        {
            for (int x = 0; x < 128; x++)
            {
                bool background = x >= 80 || y >= 80;
                bool red = ((x / 8) % 2) == 0;
                int offset = ((y * 128) + x) * 4;
                if (pixels[offset] != (background || red ? 255 : 0) || pixels[offset + 1] != (background || !red ? 255 : 0)
                    || pixels[offset + 2] != (background ? 255 : 0) || pixels[offset + 3] != 255)
                {
                    throw new InvalidDataException($"Actual UI GPU pixel differs from the independent 100-rectangle oracle at ({x},{y}).");
                }
            }
        }
    }

    private Renderer2D CreateRenderer() => new(_device, DrawingCommandRenderers2D.CreateDefault());

    private async Task<CommandBufferState> RecordAndDiscardAsync(Renderer2D renderer)
    {
        using PreparedDrawingFrame2D frame = await renderer.PrepareAsync(_snapshot, _target);
        using IGraphicsCommandBuffer commands = _device.CreateCommandBuffer(new());
        BeginTarget(commands);
        frame.Record(commands);
        commands.Finish();
        if (commands.State != CommandBufferState.Executable)
        {
            throw new InvalidOperationException("Expected executable, never-submitted native commands.");
        }

        commands.Dispose();
        frame.AbortUnsubmitted();
        return commands.State;
    }

    private async Task<byte[]> RenderPixelsAsync(Renderer2D renderer)
    {
        using PreparedDrawingFrame2D frame = await renderer.PrepareAsync(_snapshot, _target);
        using IGraphicsCommandBuffer commands = _device.CreateCommandBuffer(new());
        using IGraphicsBuffer<byte> readback = _device.CreateBuffer<byte>(new() { Count = 128 * 128 * 4, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        BeginTarget(commands);
        frame.Record(commands);
        commands.Barrier(new TextureBarrierDesc { Texture = _texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.ColorAttachment, AfterState = TextureState.CopySource, Before = new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite), After = new(PipelineStage.Copy, ResourceAccess.CopyRead) });
        commands.CopyTextureToBuffer(new() { Texture = _texture, Width = 128, Height = 128 }, new() { Buffer = readback.SliceElements(0, readback.Count), BytesPerRow = 512, RowsPerImage = 128 });
        commands.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.SliceElements(0, readback.Count), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        commands.Finish();
        using IGraphicsSubmission submission = _device.Queue.Submit([commands]);
        ValidationSubmissions++;
        frame.AttachSubmission(submission);
        await submission.WaitAsync();
        commands.Dispose();
        frame.Retire();
        await readback.MapAsync();
        try
        {
            byte[] pixels = new byte[128 * 128 * 4];
            readback.CopyTo(pixels);
            return pixels;
        }
        finally
        {
            readback.Unmap();
        }
    }

    private void BeginTarget(IGraphicsCommandBuffer commands) => commands.Barrier(new TextureBarrierDesc { Texture = _texture, Range = new(0, 1, 0, 1), BeforeState = TextureState.Undefined, AfterState = TextureState.ColorAttachment, Before = default, After = new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite) });
}
