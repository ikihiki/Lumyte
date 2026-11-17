using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.UI.WebView;

namespace Lumyte.UI.Application.WebView.Cef;

// One instance per browser surface (body/popup). Call only from UiApplication.WithGraphicsAsync.
// No cancellation after submit: completion or device retirement is mandatory before destruction.
internal sealed class WebViewTextureUploader(WebViewPixelBudget cpuBudget, WebViewPixelBudget gpuBudget) : IDisposable
{
    private readonly List<Slot> _slots = [];
    private IGraphicDevice? _device;
    private bool _busy;
    private bool _disposed;
    private Pending? _uncertain;

    internal Image2D? Current { get; private set; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_uncertain is not null || _busy || _slots.Any(slot => slot.Resource.IsReferenced))
        {
            throw new InvalidOperationException("Drain uploads and detach scenes/frames before retiring WebView images.");
        }

        _disposed = true;
        Current = null;
        foreach (Slot slot in _slots)
        {
            slot.Dispose();
        }

        _slots.Clear();
    }

    internal async ValueTask<Image2D?> TryUploadAsync(IGraphicDevice device, WebViewPixelMailbox mailbox)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_uncertain is not null)
        {
            throw new InvalidOperationException("Retire uncertain GPU work before admitting another upload.");
        }

        if (_busy)
        {
            return null;
        }

        if (_device is not null && !ReferenceEquals(_device, device))
        {
            throw new InvalidOperationException("Retire the uploader before changing graphics devices.");
        }

        _device = device;

        // A recording may survive outside a scene. Weak reachability additionally prevents changing
        // an image still held by immutable DrawList commands or a cached element recording.
        Slot? slot = _slots.FirstOrDefault(candidate => !candidate.Resource.IsReferenced && !candidate.Image.TryGetTarget(out _));
        if (slot is null && _slots.Count == 3)
        {
            return null;
        }

        WebViewPixelMailbox.PixelLease? pixels = mailbox.Take();
        if (pixels is null)
        {
            return null;
        }

        long generation = pixels.Generation;
        uint width = checked((uint)pixels.Width);
        uint height = checked((uint)pixels.Height);
        uint row;
        uint pitch;
        long stagingBytes;
        long textureBytes;
        try
        {
            TextureCopyLayout layout = device.GetTextureCopyLayout(TextureFormat.Rgba8Srgb);
            row = checked(width * 4);
            pitch = checked((uint)((checked((ulong)row + layout.BytesPerRowAlignment - 1) / layout.BytesPerRowAlignment) * layout.BytesPerRowAlignment));
            stagingBytes = checked((long)pitch * height);
            textureBytes = checked((long)row * height);
            if (textureBytes > gpuBudget.Limit || stagingBytes > cpuBudget.Limit - pixels.Bytes.Length)
            {
                throw new InvalidOperationException("webview-pixel-budget-exceeded");
            }
        }
        catch
        {
            pixels.Dispose();
            throw;
        }

        if (!cpuBudget.TryReserve(stagingBytes))
        {
            mailbox.Return(pixels);
            return null;
        }

        _busy = true;
        IGraphicsBuffer<byte>? staging = null;
        IGraphicsCommandBuffer? commands = null;
        IGraphicsSubmission? submission = null;
        bool submitted = false;
        bool retired = false;
        bool created = false;
        try
        {
            if (slot is not null && (slot.Texture.Width != width || slot.Texture.Height != height))
            {
                slot.Dispose();
                _slots.Remove(slot);
                slot = null;
            }

            if (slot is null)
            {
                if (!gpuBudget.TryReserve(textureBytes))
                {
                    mailbox.Return(pixels);
                    pixels = null;
                    return null;
                }

                try
                {
                    slot = new Slot(device, width, height, gpuBudget, textureBytes);
                    _slots.Add(slot);
                    created = true;
                }
                catch
                {
                    gpuBudget.Release(textureBytes);
                    throw;
                }
            }

            staging = device.CreateBuffer<byte>(new() { Count = checked((ulong)stagingBytes), Usage = BufferUsage.CopySource, Memory = MemoryPreference.Upload });

            await staging.MapAsync();
            try
            {
                // CEF supplies top-down premultiplied BGRA in the encoded sRGB domain.
                // Unpremultiply before sRGB decoding (sampling), then let Drawing2D premultiply in linear light.
                for (int i = 0; i < pixels.Bytes.Length; i += 4)
                {
                    byte b = pixels.Bytes[i];
                    byte g = pixels.Bytes[i + 1];
                    byte r = pixels.Bytes[i + 2];
                    int alpha = pixels.Bytes[i + 3];
                    pixels.Bytes[i] = alpha == 0 ? (byte)0 : (byte)Math.Min(255, ((r * 255) + (alpha / 2)) / alpha);
                    pixels.Bytes[i + 1] = alpha == 0 ? (byte)0 : (byte)Math.Min(255, ((g * 255) + (alpha / 2)) / alpha);
                    pixels.Bytes[i + 2] = alpha == 0 ? (byte)0 : (byte)Math.Min(255, ((b * 255) + (alpha / 2)) / alpha);
                }

                for (int y = 0; y < pixels.Height; y++)
                {
                    staging.CopyBytesFrom(pixels.Bytes.AsSpan(checked(y * (int)row), (int)row), checked((ulong)y * pitch), row);
                }
            }
            finally
            {
                // CPU access must end before this buffer can participate in GPU commands.
                staging.Unmap();
            }

            // The CEF callback's owned CPU copy is no longer needed after copying into staging.
            pixels.Dispose();
            pixels = null;
            commands = device.CreateCommandBuffer(new());
            commands.Barrier(new BufferBarrierDesc<byte>
            {
                Buffer = staging.SliceElements(0, checked((ulong)stagingBytes)),
                Before = new(PipelineStage.Host, ResourceAccess.HostWrite),
                After = new(PipelineStage.Copy, ResourceAccess.CopyRead),
            });
            commands.Barrier(new TextureBarrierDesc
            {
                Texture = slot.Texture,
                Range = new(0, 1, 0, 1),
                BeforeState = created ? TextureState.Undefined : TextureState.Sampled,
                AfterState = TextureState.CopyDestination,
                Before = created ? new(PipelineStage.None, ResourceAccess.None) : new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead),
                After = new(PipelineStage.Copy, ResourceAccess.CopyWrite),
            });
            commands.CopyBufferToTexture(new() { Buffer = staging.SliceElements(0, checked((ulong)stagingBytes)), BytesPerRow = pitch, RowsPerImage = height }, new() { Texture = slot.Texture, Width = width, Height = height });
            commands.Barrier(new TextureBarrierDesc
            {
                Texture = slot.Texture,
                Range = new(0, 1, 0, 1),
                BeforeState = TextureState.CopyDestination,
                AfterState = TextureState.Sampled,
                Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite),
                After = new(PipelineStage.FragmentShader, ResourceAccess.ShaderRead),
            });
            commands.Finish();

            // Treat a throwing submit as ambiguous native work, not an unsubmitted discard.
            submitted = true;
            submission = device.Queue.Submit([commands]);
            await submission.WaitAsync();
            if (submission.Status != SubmissionStatus.Completed)
            {
                throw new InvalidOperationException("WebView texture upload did not complete.");
            }

            retired = true;
            if (!mailbox.IsCurrent(generation))
            {
                return null;
            }

            Image2D image = new(slot.View, slot.Sampler, AlphaRepresentation2D.Straight);
            slot.Resource = ImageResourceRegistry2D.Register(image);
            slot.Image.SetTarget(image);
            Current = image;
            return image;
        }
        catch
        {
            if (submitted && !retired)
            {
                try
                {
                    await device.WaitForRetirementAsync();
                    retired = true;
                }
                catch
                {
                    _uncertain = new(submission, commands, staging, stagingBytes);
                    throw;
                }
            }

            // Failed commands may have changed texture state. Never reuse their destination.
            if (slot is not null)
            {
                slot.Dispose();
                _slots.Remove(slot);
            }

            throw;
        }
        finally
        {
            // A failed retirement wait deliberately retains native allocations and budget.
            // Such a failure must keep the window in StopFailed rather than destroying live work.
            if (!submitted || retired)
            {
                submission?.Dispose();
                commands?.Dispose();
                staging?.Dispose();
                cpuBudget.Release(stagingBytes);
            }

            pixels?.Dispose();
            _busy = false;
        }
    }

    internal async ValueTask RetireAsync()
    {
        if (_busy)
        {
            throw new InvalidOperationException("Await the active upload before retirement.");
        }

        if (_uncertain is { } pending)
        {
            await _device!.WaitForRetirementAsync();
            pending.Submission?.Dispose();
            pending.Commands?.Dispose();
            pending.Staging?.Dispose();
            cpuBudget.Release(pending.Bytes);
            _uncertain = null;
        }

        Dispose();
    }

    private sealed record Pending(IGraphicsSubmission? Submission, IGraphicsCommandBuffer? Commands, IGraphicsBuffer<byte>? Staging, long Bytes);

    private sealed class Slot : IDisposable
    {
        private readonly WebViewPixelBudget _budget;
        private readonly long _bytes;

        internal Slot(IGraphicDevice device, uint width, uint height, WebViewPixelBudget budget, long bytes)
        {
            _budget = budget;
            _bytes = bytes;
            Texture = device.CreateTexture(new() { Width = width, Height = height, Format = TextureFormat.Rgba8Srgb, Usage = TextureUsage.Sampled | TextureUsage.CopyDestination });
            try
            {
                View = Texture.CreateView();
                try
                {
                    Sampler = device.CreateSampler(new() { AddressU = AddressMode.ClampToEdge, AddressV = AddressMode.ClampToEdge });
                }
                catch
                {
                    View.Dispose();
                    throw;
                }
            }
            catch
            {
                Texture.Dispose();
                throw;
            }
        }

        internal IGraphicsTexture Texture { get; }

        internal IGraphicsTextureView View { get; }

        internal IGraphicsSampler Sampler { get; }

        internal ImageResourceRegistry2D.Resource Resource { get; set; } = new();

        internal WeakReference<Image2D> Image { get; } = new(null!);

        public void Dispose()
        {
            View.Dispose();
            Texture.Dispose();
            Sampler.Dispose();
            _budget.Release(_bytes);
        }
    }
}
