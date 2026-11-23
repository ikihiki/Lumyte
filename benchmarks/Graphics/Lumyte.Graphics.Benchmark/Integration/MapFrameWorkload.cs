using Lumyte.Benchmarks.GpuMaps;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Vulkan;
using Lumyte.Maps.UI;
using Lumyte.UI;

namespace Lumyte.Graphics.Benchmark.Integration;

/// <summary>Headless, warm-renderer, completed logical frames; no presentation or GPU timestamp attribution.</summary>
public sealed class MapFrameWorkload : IAsyncDisposable
{
    /// <summary>The identical logical frame count at both queue depths.</summary>
    public const int FramesPerOperation = 6;

    private const uint Size = 512;
    private readonly VulkanDevice _device;
    private readonly Renderer2D _renderer;
    private readonly Slot[] _slots;
    private readonly DrawList2D _staticDrawing;
    private readonly MapWorkload _map;
    private readonly UiSurface _surface;
    private readonly UiDrawingScene2D _ui;
    private readonly bool _retained;
    private readonly bool _moving;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="MapFrameWorkload"/> class outside measurement.</summary>
    /// <param name="retained">Use the actual MapView retained scene.</param>
    /// <param name="moving">Advance fixed pan/continuous zoom input.</param>
    /// <param name="depth">Maximum outstanding frame submissions.</param>
    public MapFrameWorkload(bool retained, bool moving, int depth)
    {
        if (depth is not (1 or 3))
        {
            throw new ArgumentOutOfRangeException(nameof(depth));
        }

        Identity = HardwareVulkan.Verify();
        _retained = retained;
        _moving = moving;
        _map = new();
        _map.WarmAsync().GetAwaiter().GetResult();
        _surface = new(new MapView { Session = _map.Session });
        _surface.SetViewport(new(512, 512));
        _ui = _surface.CreateDrawingScene();
        _ui.Update();
        _ui.Update();
        if (_ui.LastRecordingCount != 0)
        {
            throw new InvalidOperationException("Static MapView did not retain its recording.");
        }

        _staticDrawing = _map.Record();
        _device = VulkanDevice.Create(physicalDeviceIndex: 0);
        _renderer = new(_device, DrawingCommandRenderers2D.CreateDefault());
        _slots = Enumerable.Range(0, depth).Select(_ => new Slot(_device)).ToArray();
    }

    /// <summary>Gets independently verified physical-device-zero identity.</summary>
    public string Identity { get; }

    /// <summary>Gets the completed operation's logical frame count.</summary>
    public int CompletedFrames { get; private set; }

    /// <summary>Gets the final logical frame index, set only after the final drain.</summary>
    public int LastCompletedFrame { get; private set; } = -1;

    /// <summary>Gets the largest outstanding submission count observed in the last operation.</summary>
    public int PeakOutstanding { get; private set; }

    /// <summary>Prepares, records, submits and completes six frames, including safe retirement and final drain.</summary>
    /// <returns>Completion of all six logical frames.</returns>
    public async Task RunAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CompletedFrames = 0;
        LastCompletedFrame = -1;
        PeakOutstanding = 0;
        int outstanding = 0;
        for (int frame = 0; frame < FramesPerOperation; frame++)
        {
            Slot slot = _slots[frame % _slots.Length];
            if (slot.Submission is not null)
            {
                await RetireAsync(slot).ConfigureAwait(false);
                outstanding--;
            }

            if (_moving)
            {
                _map.Move(frame);
            }

            var target = new RenderTarget2D { View = slot.View, LoadOp = AttachmentLoadOp.Clear, ClearColor = new(0, 0, 0, 1) };
            if (_retained)
            {
                _ui.Update();
                if (_ui.LastRecordingCount != (_moving ? 1 : 0))
                {
                    throw new InvalidOperationException("MapView recording eligibility did not match the input profile.");
                }

                using DrawingSceneReadLease2D source = _ui.Scene.Borrow();
                slot.Retained = await _renderer.PrepareAsync(source, target).ConfigureAwait(false);
            }
            else
            {
                slot.Prepared = await _renderer.PrepareAsync(_moving ? _map.Record() : _staticDrawing, target).ConfigureAwait(false);
            }

            slot.Commands = _device.CreateCommandBuffer(new());
            Transition(slot.Commands, slot.Texture, slot.Initialized ? TextureState.ColorAttachment : TextureState.Undefined, TextureState.ColorAttachment, slot.Initialized ? new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite) : default, new(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite));
            if (slot.Retained is { } retained)
            {
                retained.Record(slot.Commands);
            }
            else
            {
                slot.Prepared!.Record(slot.Commands);
            }

            slot.Commands.Finish();
            slot.Submission = _device.Queue.Submit([slot.Commands]);
            slot.Retained?.AttachSubmission(slot.Submission);
            slot.Frame = frame;
            slot.Initialized = true;
            outstanding++;
            PeakOutstanding = Math.Max(PeakOutstanding, outstanding);
        }

        foreach (Slot slot in _slots)
        {
            await RetireAsync(slot).ConfigureAwait(false);
        }
    }

    /// <summary>Reads the last completed logical frame outside the measured operation and checks analytic pixels.</summary>
    /// <returns>Completion of the functional pixel oracle.</returns>
    public async Task VerifyLastFrameAsync()
    {
        if (CompletedFrames != FramesPerOperation || LastCompletedFrame != FramesPerOperation - 1)
        {
            throw new InvalidOperationException("Pixel verification requires a fully completed operation.");
        }

        Slot slot = _slots[(FramesPerOperation - 1) % _slots.Length];
        TextureCopyLayout layout = _device.GetTextureCopyLayout(TextureFormat.Rgba8Srgb);
        uint pitch = (((Size * 4) + layout.BytesPerRowAlignment - 1) / layout.BytesPerRowAlignment) * layout.BytesPerRowAlignment;
        using IGraphicsBuffer<byte> readback = _device.CreateBuffer<byte>(new() { Count = pitch * Size, Usage = BufferUsage.CopyDestination, Memory = MemoryPreference.Readback });
        using IGraphicsCommandBuffer commands = _device.CreateCommandBuffer(new());
        var color = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorWrite);
        var copy = new BarrierScope(PipelineStage.Copy, ResourceAccess.CopyRead);
        Transition(commands, slot.Texture, TextureState.ColorAttachment, TextureState.CopySource, color, copy);
        commands.CopyTextureToBuffer(new() { Texture = slot.Texture, Width = Size, Height = Size }, new() { Buffer = readback.SliceElements(0, readback.Count), BytesPerRow = pitch, RowsPerImage = Size });
        commands.Barrier(new BufferBarrierDesc<byte> { Buffer = readback.SliceElements(0, readback.Count), Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite), After = new(PipelineStage.Host, ResourceAccess.HostRead) });
        Transition(commands, slot.Texture, TextureState.CopySource, TextureState.ColorAttachment, copy, color);
        commands.Finish();
        using IGraphicsSubmission submission = _device.Queue.Submit([commands]);
        await submission.WaitAsync().ConfigureAwait(false);
        await readback.MapAsync().ConfigureAwait(false);
        byte[] pixels = new byte[checked((int)readback.Count)];
        readback.CopyTo(pixels);
        readback.Unmap();

        // Transform independent fixture interiors analytically, never through the map recorder.
        // The last path sample is fixed (frame 5), so the oracle is deterministic at both depths.
        double scale = _moving ? Math.Pow(2, 5 / 32.0) : 1;
        double dx = _moving ? -3 : 0;
        double dy = _moving ? 2 : 0;
        (int X, int Y) Position(double x, double y) =>
            ((int)Math.Floor(256 + ((x + dx - 256) * scale)), (int)Math.Floor(256 + ((y + dy - 256) * scale)));
        (int X, int Y) building = Position(140, 140);
        (int X, int Y) water = Position(250, 46);
        (int X, int Y) ground = Position(240, 470);
        CheckPixel(pixels, pitch, building.X, building.Y, 255, 0, 0);
        CheckPixel(pixels, pitch, water.X, water.Y, 0, 0, 255);
        CheckPixel(pixels, pitch, ground.X, ground.Y, 0, 0, 0);
    }

    /// <summary>Drains any outstanding submissions before disposing frame owners, targets, renderer and device.</summary>
    /// <returns>Completion-safe teardown.</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (Slot slot in _slots)
        {
            await RetireAsync(slot).ConfigureAwait(false);
            slot.View.Dispose();
            slot.Texture.Dispose();
        }

        _renderer.Dispose();
        _ui.Dispose();
        _surface.Dispose();
        await _map.DisposeAsync();
        _device.Dispose();
    }

    private static void CheckPixel(byte[] pixels, uint pitch, int x, int y, byte r, byte g, byte b)
    {
        int offset = checked((int)((y * pitch) + (x * 4)));
        if (pixels[offset] != r || pixels[offset + 1] != g || pixels[offset + 2] != b || pixels[offset + 3] != 255)
        {
            throw new InvalidOperationException($"Analytic map GPU pixel mismatch at ({x},{y}): {string.Join(',', pixels.AsSpan(offset, 4).ToArray())}.");
        }
    }

    private static void Transition(IGraphicsCommandBuffer commands, IGraphicsTexture texture, TextureState before, TextureState after, BarrierScope source, BarrierScope destination)
        => commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = before, AfterState = after, Before = source, After = destination });

    private async ValueTask RetireAsync(Slot slot)
    {
        if (slot.Submission is not null)
        {
            await slot.Submission.WaitAsync().ConfigureAwait(false);
            if (slot.Submission.Status != SubmissionStatus.Completed)
            {
                throw new InvalidOperationException("GPU submission did not complete successfully.");
            }

            slot.Commands?.Dispose();
            slot.Commands = null;
            slot.Retained?.Retire();
            slot.Submission.Dispose();
            slot.Submission = null;
            CompletedFrames++;
            LastCompletedFrame = Math.Max(LastCompletedFrame, slot.Frame);
        }

        slot.Commands?.Dispose();
        slot.Commands = null;
        slot.Retained?.Dispose();
        slot.Retained = null;
        slot.Prepared?.Dispose();
        slot.Prepared = null;
    }

    private sealed class Slot
    {
        public Slot(IGraphicDevice device)
        {
            Texture = device.CreateTexture(new() { Width = Size, Height = Size, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource });
            View = Texture.CreateView();
        }

        public IGraphicsTexture Texture { get; }

        public IGraphicsTextureView View { get; }

        public IGraphicsCommandBuffer? Commands { get; set; }

        public PreparedDrawingFrame2D? Retained { get; set; }

        public PreparedDraw2D? Prepared { get; set; }

        public IGraphicsSubmission? Submission { get; set; }

        public bool Initialized { get; set; }

        public int Frame { get; set; }
    }
}
