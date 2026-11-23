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
    private List<(Slot Slot, ImageResourceRegistry2D.Resource.WriteLease Writer)>? _retiring;

    internal Image2D? Current { get; private set; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_retiring is null)
        {
            if (_uncertain is not null || _busy || _slots.Any(slot => slot.Resource.IsReferenced))
            {
                throw new InvalidOperationException("Drain uploads and detach scenes/frames before retiring WebView images.");
            }

            var writers = new List<(Slot Slot, ImageResourceRegistry2D.Resource.WriteLease Writer)>();
            try
            {
                foreach (Slot slot in _slots)
                {
                    ImageResourceRegistry2D.Resource.WriteLease writer = slot.Resource.TryAcquireWrite() ?? throw new InvalidOperationException("An image reader still owns this WebView allocation.");
                    writers.Add((slot, writer));
                }

                // Transfer every gate before cleanup: a throwing native Dispose must leave a retry owner.
                _retiring = writers;
            }
            finally
            {
                if (_retiring is null)
                {
                    foreach ((Slot Slot, ImageResourceRegistry2D.Resource.WriteLease Writer) entry in writers)
                    {
                        entry.Writer.Dispose();
                    }
                }
            }
        }

        // Invalidate every old CPU descriptor before destroying any of their borrowed views.
        foreach ((Slot Slot, ImageResourceRegistry2D.Resource.WriteLease Writer) entry in _retiring)
        {
            entry.Writer.Invalidate();
        }

        Current = null;
        while (_retiring.Count != 0)
        {
            (Slot slot, ImageResourceRegistry2D.Resource.WriteLease writer) = _retiring[0];
            slot.Dispose(writer);
            _slots.Remove(slot);
            writer.Dispose();
            _retiring.RemoveAt(0);
        }

        _retiring = null;
        _disposed = true;
    }

    internal async ValueTask<Image2D?> TryUploadAsync(IGraphicDevice device, WebViewPixelMailbox mailbox)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_uncertain is not null || _retiring is not null)
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

        // Browser CPU recordings borrow live pixels. Explicit consumers pin the allocation, not wrappers.
        // Never overwrite the currently published image while admitting its replacement.
        Slot? slot = null;
        ImageResourceRegistry2D.Resource.WriteLease? writer = null;
        foreach (Slot candidate in _slots)
        {
            if (!ReferenceEquals(candidate.View, Current?.View) && candidate.Resource.TryAcquireWrite() is { } admitted)
            {
                slot = candidate;
                writer = admitted;
                break;
            }
        }

        try
        {
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
            bool discardSlot = true;
            try
            {
                if (slot is not null && (slot.Texture.Width != width || slot.Texture.Height != height))
                {
                    slot.Dispose(writer!);
                    writer!.Dispose();
                    writer = null;
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
                        writer = slot.Resource.TryAcquireWrite()!;
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
                discardSlot = false;
                if (!mailbox.IsCurrent(generation))
                {
                    return null;
                }

                Image2D image = new(slot.View, slot.Sampler, AlphaRepresentation2D.Straight);
                ImageResourceRegistry2D.Register(image, slot.Resource);
                slot.Image.SetTarget(image);
                Current = image;
                return image;
            }
            catch
            {
                discardSlot = true;
                throw;
            }
            finally
            {
                // The pending owner checkpoints every successful cleanup, including budget returns.
                // An ambiguous submit or throwing Dispose retains its writer and unfinished children.
                var pending = new Pending(submission, commands, staging, stagingBytes, slot, writer, discardSlot, submitted && !retired);
                _uncertain = pending;
                writer = null;
                try
                {
                    await CompletePendingAsync(pending);
                }
                finally
                {
                    pixels?.Dispose();
                    _busy = false;
                }
            }
        }
        finally
        {
            writer?.Dispose();
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
            await CompletePendingAsync(pending);
        }

        Dispose();
    }

    private async ValueTask CompletePendingAsync(Pending pending)
    {
        if (pending.NeedsDeviceRetirement)
        {
            await _device!.WaitForRetirementAsync();
            pending.NeedsDeviceRetirement = false;
        }

        pending.Cleanup(cpuBudget);
        if (pending.RetireSlot && pending.Slot is { } slot)
        {
            _slots.Remove(slot);
        }

        _uncertain = null;
    }

    private sealed class Pending(IGraphicsSubmission? submission, IGraphicsCommandBuffer? commands, IGraphicsBuffer<byte>? staging, long bytes, Slot? slot, ImageResourceRegistry2D.Resource.WriteLease? writer, bool retireSlot, bool needsDeviceRetirement)
    {
        private IGraphicsSubmission? _submission = submission;
        private IGraphicsCommandBuffer? _commands = commands;
        private IGraphicsBuffer<byte>? _staging = staging;
        private ImageResourceRegistry2D.Resource.WriteLease? _writer = writer;
        private bool _invalidated;
        private bool _cpuReleased;

        internal Slot? Slot { get; } = slot;

        internal bool RetireSlot { get; } = retireSlot;

        internal bool NeedsDeviceRetirement { get; set; } = needsDeviceRetirement;

        internal void Cleanup(WebViewPixelBudget budget)
        {
            if (RetireSlot && !_invalidated)
            {
                _writer?.Invalidate();
                _invalidated = true;
            }

            _submission?.Dispose();
            _submission = null;
            _commands?.Dispose();
            _commands = null;
            _staging?.Dispose();
            _staging = null;
            if (!_cpuReleased)
            {
                budget.Release(bytes);
                _cpuReleased = true;
            }

            if (RetireSlot)
            {
                Slot?.Dispose(_writer!);
            }

            _writer?.Dispose();
            _writer = null;
        }
    }

    private sealed class Slot
    {
        private readonly WebViewPixelBudget _budget;
        private readonly long _bytes;
        private bool _viewDisposed;
        private bool _textureDisposed;
        private bool _samplerDisposed;
        private bool _budgetReleased;

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

        internal ImageResourceRegistry2D.Resource Resource { get; } = new();

        internal WeakReference<Image2D> Image { get; } = new(null!);

        internal void Dispose(ImageResourceRegistry2D.Resource.WriteLease writer)
        {
            writer.Invalidate();
            if (!_viewDisposed)
            {
                View.Dispose();
                _viewDisposed = true;
            }

            if (!_textureDisposed)
            {
                Texture.Dispose();
                _textureDisposed = true;
            }

            if (!_samplerDisposed)
            {
                Sampler.Dispose();
                _samplerDisposed = true;
            }

            if (!_budgetReleased)
            {
                _budget.Release(_bytes);
                _budgetReleased = true;
            }
        }
    }
}
