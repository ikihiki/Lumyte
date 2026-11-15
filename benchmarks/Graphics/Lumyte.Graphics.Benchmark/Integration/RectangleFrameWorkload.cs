using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Vulkan;
using Lumyte.Mathematics.Colors;

namespace Lumyte.Graphics.Benchmark.Integration;

/// <summary>Controls input recording; preparation and GPU execution occur for every frame in all profiles.</summary>
public enum RectangleInputProfile
{
    /// <summary>Reuses an immutable drawing recorded outside measurement.</summary>
    StaticReuse,

    /// <summary>Rebuilds the drawing inside each frame with only the last rectangle changing color.</summary>
    OneChanged,

    /// <summary>Rebuilds the drawing inside each frame with every rectangle changing color.</summary>
    AllChanged,
}

/// <summary>Headless, warm-renderer, completed logical frames; no presentation or GPU timestamp attribution.</summary>
public sealed class RectangleFrameWorkload : IAsyncDisposable
{
    /// <summary>The identical logical frame count at both queue depths.</summary>
    public const int FramesPerOperation = 6;

    private const uint Size = 256;
    private readonly VulkanDevice _device;
    private readonly Renderer2D _renderer;
    private readonly Slot[] _slots;
    private readonly DrawList2D _staticDrawing;
    private readonly int _count;
    private readonly RectangleInputProfile _profile;
    private bool _disposed;

    /// <summary>Initializes a new instance of the <see cref="RectangleFrameWorkload"/> class outside measurement.</summary>
    /// <param name="count">Rectangle count.</param>
    /// <param name="profile">Input update profile.</param>
    /// <param name="depth">Maximum outstanding frame submissions.</param>
    public RectangleFrameWorkload(int count, RectangleInputProfile profile, int depth)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, 128 * 128);
        if (depth is not (1 or 3))
        {
            throw new ArgumentOutOfRangeException(nameof(depth));
        }

        Identity = HardwareVulkan.Verify();
        _count = count;
        _profile = profile;
        _staticDrawing = BuildDrawing(0);
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

            DrawList2D drawing = _profile == RectangleInputProfile.StaticReuse ? _staticDrawing : BuildDrawing(frame);
            slot.Prepared = await _renderer.PrepareAsync(drawing, new() { View = slot.View, LoadOp = AttachmentLoadOp.Clear, ClearColor = new(0, 0, 0, 1) }).ConfigureAwait(false);
            slot.Commands = _device.CreateCommandBuffer(new());
            Transition(slot.Commands, slot.Texture, slot.Initialized ? TextureState.ColorAttachment : TextureState.Undefined, TextureState.ColorAttachment, slot.Initialized ? new(PipelineStage.ColorOutput, ResourceAccess.ColorWrite) : default, new(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite));
            slot.Prepared.Record(slot.Commands);
            slot.Commands.Finish();
            slot.Submission = _device.Queue.Submit([slot.Commands]);
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

        // Each 1x1 tile is analytically opaque red or green; all uncovered pixels are opaque black.
        // This checks every rendered rectangle, including later implementation batches, and the whole clear area.
        for (int y = 0; y < Size; y++)
        {
            for (int x = 0; x < Size; x++)
            {
                int rectangle = (y * 128) + x;
                bool covered = x < 128 && rectangle < _count;
                bool changed = covered && (_profile == RectangleInputProfile.AllChanged || (_profile == RectangleInputProfile.OneChanged && rectangle == _count - 1));
                CheckPixel(pixels, pitch, x, y, red: covered && !changed, green: changed);
            }
        }
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
        _device.Dispose();
    }

    private static void CheckPixel(byte[] pixels, uint pitch, int x, int y, bool red, bool green)
    {
        int offset = checked((int)((y * pitch) + (x * 4)));
        if (pixels[offset] != (red ? 255 : 0) || pixels[offset + 1] != (green ? 255 : 0) || pixels[offset + 2] != 0 || pixels[offset + 3] != 255)
        {
            throw new InvalidOperationException($"Analytic GPU pixel mismatch at ({x},{y}): {string.Join(',', pixels.AsSpan(offset, 4).ToArray())}.");
        }
    }

    private static void Transition(IGraphicsCommandBuffer commands, IGraphicsTexture texture, TextureState before, TextureState after, BarrierScope source, BarrierScope destination)
        => commands.Barrier(new TextureBarrierDesc { Texture = texture, Range = new(0, 1, 0, 1), BeforeState = before, AfterState = after, Before = source, After = destination });

    private DrawList2D BuildDrawing(int frame)
    {
        var canvas = new Canvas2D();
        canvas.Begin();
        for (int i = 0; i < _count; i++)
        {
            bool changed = (frame & 1) != 0 && (_profile == RectangleInputProfile.AllChanged || (_profile == RectangleInputProfile.OneChanged && i == _count - 1));
            canvas.FillRect(new(i % 128, i / 128, 1, 1), changed ? new SRgba<float>(0, 1, 0, 1) : new SRgba<float>(1, 0, 0, 1));
        }

        return canvas.End();
    }

    private async ValueTask RetireAsync(Slot slot)
    {
        if (slot.Submission is not null)
        {
            await slot.Submission.WaitAsync().ConfigureAwait(false);
            if (slot.Submission.Status != SubmissionStatus.Completed)
            {
                throw new InvalidOperationException("GPU submission did not complete successfully.");
            }

            slot.Submission.Dispose();
            slot.Submission = null;
            CompletedFrames++;
            LastCompletedFrame = Math.Max(LastCompletedFrame, slot.Frame);
        }

        slot.Commands?.Dispose();
        slot.Commands = null;
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

        public PreparedDraw2D? Prepared { get; set; }

        public IGraphicsSubmission? Submission { get; set; }

        public bool Initialized { get; set; }

        public int Frame { get; set; }
    }
}
