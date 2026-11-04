using System.Diagnostics;
using System.Security.Cryptography;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Incremental.Benchmarks;
using Lumyte.Graphics.Vulkan;

namespace Lumyte.Graphics.UiScenarios;

internal static class UiGpuRunner
{
    // Unknown submission acceptance cannot be made safe by ordinary Dispose.
    internal static readonly List<object> Quarantine = [];

    internal static async Task RunAsync(VulkanDevice device, UiProfile profile, int warmup, int samples, int depth, Action<object> emit)
    {
        var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
        Slot[] slots = Enumerable.Range(0, depth).Select(_ => new Slot(device)).ToArray();
        using var fixture = new UiScenario(profile);
        var rows = new List<object>(samples);
        try
        {
            emit(new
            {
                scope = "ui-preflight",
                profile.Key,
                fixture.FontHash,
                fixture.NativeFontInitializationMs,
                fixture.InitialMountAndLayoutMs,
                firstTextLayoutMs = fixture.TextLayoutTicks * 1000.0 / Stopwatch.Frequency,
                firstTextLayoutScope = "nested in initial mount/layout; do not add twice",
                reference = "fresh independently mounted UiSurface from logical input, direct Canvas; no Scene flatten/replay",
                analytic = "opaque red sentinel outside UI and preview",
            });

            // Selected states include initial, real changes, row turnover, DPI and UI-input events.
            foreach (int frame in new[]
            {
                0,
                6,
                12,
                30,
                60,
                61,
            })
            {
                fixture.Update(frame);
                fixture.Layout();
                emit(fixture.Verify());
                await CheckAsync(device, renderer, fixture, emit, firstUse: frame == 0);
            }

            // Fresh fixture for timing: preflight must not leave a future logical state in the trace.
            using var measured = new UiScenario(profile);
            for (int i = 0; i < warmup; i++)
            {
                await SubmitFrameAsync(renderer, measured, slots[i % depth], i, false, rows);
            }

            foreach (Slot slot in slots)
            {
                await slot.CompleteAsync();
                slot.ResetFrame();
            }

            // Replay the fixed trace from logical frame zero after warming; one-time events stay measured.
            measured.Update(0);
            measured.Layout();
            long throughputStart = Stopwatch.GetTimestamp();
            for (int i = 0; i < samples; i++)
            {
                await SubmitFrameAsync(renderer, measured, slots[i % depth], i, true, rows);
            }

            foreach (Slot slot in slots)
            {
                await slot.CompleteAsync();
            }

            double elapsed = Ms(throughputStart);
            foreach (object row in rows)
            {
                emit(row);
            }

            foreach (Slot slot in slots.Where(s => s.Frame >= 0))
            {
                emit(new
                {
                    scope = "completion",
                    profile.Key,
                    slot.Frame,
                    slot.InputId,
                    slot.CompletionMs,
                    slot.RetireMs,
                    gpuSpanMs = slot.GpuSpanMs,
                    gpuQueryStatus = slot.GpuQueryStatus,
                });
            }

            // Verify final state independently after drain, outside timing.
            emit(measured.Verify());
            await CheckAsync(device, renderer, measured, emit);
            emit(new
            {
                scope = "pipeline-summary",
                profile.Key,
                depth,
                samples,
                warmup,
                elapsedMs = elapsed,
                fps = samples * 1000.0 / elapsed,
                includesFinalDrain = true,
                includesRetirement = true,
                presentation = false,
                timestampSupported = slots[0].TimestampSupported,
                gpuSpanSource = "completion rows joined by profile key/frame/input ID; overlapping spans are not summed as GPU busy time",
                counters = measured.Counters(),
            });
        }
        finally
        {
            if (Quarantine.Count != 0 || slots.Any(s => s.Uncertain || s.Submission is not null))
            {
                Quarantine.AddRange(new object[]
                {
                    device,
                    renderer,
                    slots,
                    fixture,
                });
            }
            else
            {
                foreach (Slot slot in slots)
                {
                    slot.Dispose();
                }

                renderer.Dispose();
            }
        }
    }

    private static async Task SubmitFrameAsync(Renderer2D renderer, UiScenario fixture, Slot slot, int frame, bool save, List<object> rows, int measuredStart = 0)
    {
        long wait = Stopwatch.GetTimestamp();
        await slot.CompleteAsync();
        double slotWaitAndRetireMs = Ms(wait);

        // Capture completion of the previous slot frame before its ID is replaced.
        if (save && slot.Frame >= measuredStart)
        {
            rows.Add(new
            {
                scope = "completion",
                fixture.Profile.Key,
                slot.Frame,
                slot.InputId,
                slot.CompletionMs,
                slot.RetireMs,
                gpuSpanMs = slot.GpuSpanMs,
                gpuQueryStatus = slot.GpuQueryStatus,
            });
        }

        long start = Stopwatch.GetTimestamp();
        long stage = Stopwatch.GetTimestamp();
        fixture.Update(frame);
        double updateMs = Ms(stage);
        stage = Stopwatch.GetTimestamp();
        fixture.Layout();
        double layoutMs = Ms(stage);
        stage = Stopwatch.GetTimestamp();
        IReadOnlyList<DrawList2D> lists = fixture.Record();
        double recordMs = Ms(stage);
        stage = Stopwatch.GetTimestamp();
        slot.Resize((uint)fixture.Input.TargetWidth, (uint)fixture.Input.TargetHeight);
        double targetResizeMs = Ms(stage);
        stage = Stopwatch.GetTimestamp();
        await slot.PrepareAsync(renderer, lists);
        double prepareMs = Ms(stage);
        stage = Stopwatch.GetTimestamp();
        slot.Encode();
        double encodeMs = Ms(stage);
        stage = Stopwatch.GetTimestamp();
        slot.Submit(frame, fixture.Input.InputId);
        double submitMs = Ms(stage);
        double hostFrameMs = Ms(start);
        if (save)
        {
            rows.Add(new
            {
                scope = "frame",
                scenarioVersion = "ui-v2",
                fixture.Profile.Key,
                frame,
                inputId = fixture.Input.InputId,
                logicalSeconds = (double)frame / fixture.Profile.Hz,
                updateMs,
                layoutMs,
                recordMs,
                targetResizeMs,
                prepareMs,
                encodeMs,
                submitMs,
                hostFrameMs,
                slotWaitAndRetireMs,
                gpuQueryPending = slot.TimestampSupported,
                gpuSpanSource = "matching completion row after confirmed fence",
                issuedDraws = slot.DrawCalls,
                commands = lists.Sum(l => l.Count),
                targetReallocated = slot.Reallocated,
                counters = fixture.Counters(),
            });
        }
    }

    private static async Task CheckAsync(VulkanDevice device, Renderer2D renderer, UiScenario fixture, Action<object> emit, bool firstUse = false)
    {
        using var actual = new Slot(device);
        using var expected = new Slot(device);
        try
        {
            actual.Resize((uint)fixture.Input.TargetWidth, (uint)fixture.Input.TargetHeight);
            expected.Resize((uint)fixture.Input.TargetWidth, (uint)fixture.Input.TargetHeight);
            long recordStart = Stopwatch.GetTimestamp();
            IReadOnlyList<DrawList2D> recording = fixture.Record();
            double recordMs = Ms(recordStart);
            long prepareStart = Stopwatch.GetTimestamp();
            await actual.PrepareAsync(renderer, recording);
            double prepareMs = Ms(prepareStart);
            long encodeStart = Stopwatch.GetTimestamp();
            actual.Encode();
            double encodeMs = Ms(encodeStart);
            long submitStart = Stopwatch.GetTimestamp();
            actual.Submit(fixture.Input.Frame, fixture.Input.InputId);
            double submitMs = Ms(submitStart);
            await actual.CompleteAsync();
            await expected.PrepareAsync(renderer, fixture.Reference());
            expected.Encode();
            expected.Submit(fixture.Input.Frame, fixture.Input.InputId);
            await expected.CompleteAsync();
            byte[] a = await actual.ReadAsync();
            byte[] e = await expected.ReadAsync();
            if (!a.AsSpan().SequenceEqual(e))
            {
                throw new InvalidOperationException("Independent UI exact pixels differ: " + fixture.Profile.Key + "/" + fixture.Input.Frame);
            }

            int offset = checked((((fixture.Input.TargetHeight - 12) * fixture.Input.TargetWidth) + fixture.Input.TargetWidth - 12) * 4);
            int blue = checked((((fixture.Input.TargetHeight - 13) * fixture.Input.TargetWidth) + fixture.Input.TargetWidth - 13) * 4);

            // Bounded synthetic pixel evidence goes only to the caller-owned private check log.
            // The independent frame equality and exact analytic assertions remain mandatory.
            emit(new
            {
                check = "ui-analytic-probe-values",
                fixture.Profile.Key,
                fixture.Input.Frame,
                fixture.Input.InputId,
                independentPixelsEqual = true,
                redPixelX = fixture.Input.TargetWidth - 12,
                redPixelY = fixture.Input.TargetHeight - 12,
                redActual = a.AsSpan(offset, 4).ToArray().Select(value => (int)value).ToArray(),
                redReference = e.AsSpan(offset, 4).ToArray().Select(value => (int)value).ToArray(),
                bluePixelX = fixture.Input.TargetWidth - 13,
                bluePixelY = fixture.Input.TargetHeight - 13,
                blueActual = a.AsSpan(blue, 4).ToArray().Select(value => (int)value).ToArray(),
                blueReference = e.AsSpan(blue, 4).ToArray().Select(value => (int)value).ToArray(),
            });
            if (a[offset] != 255 || a[offset + 1] != 0 || a[offset + 2] != 0 || a[offset + 3] != 255)
            {
                throw new InvalidOperationException("Analytic red sentinel mismatch.");
            }

            if (a[blue] != 0 || a[blue + 1] != 0 || a[blue + 2] != 255 || a[blue + 3] != 255)
            {
                throw new InvalidOperationException("Analytic clip/order sentinel mismatch.");
            }

            emit(new
            {
                check = "ui-independent-exact-pixels",
                fixture.Profile.Key,
                fixture.Input.Frame,
                fixture.Input.InputId,
                sha256 = Convert.ToHexString(SHA256.HashData(a)),
                analyticSentinel = true,
                firstRenderStages = firstUse ? new
                {
                    recordMs,
                    prepareMs,
                    encodeMs,
                    submitMs,
                    completionObservationMs = actual.CompletionMs,
                    retirementMs = actual.RetireMs,
                    gpuSpanMs = actual.GpuSpanMs,
                }

                : null,
                actualGpuSpanMs = actual.GpuSpanMs,
                referenceGpuSpanMs = expected.GpuSpanMs,
                status = "passed",
            });
        }
        catch
        {
            if (actual.Uncertain || expected.Uncertain)
            {
                Quarantine.AddRange(new object[]
                {
                    device,
                    renderer,
                    actual,
                    expected,
                });
            }

            throw;
        }
    }

    private static double Ms(long t) => Stopwatch.GetElapsedTime(t).TotalMilliseconds;

    private sealed class Slot(VulkanDevice device) : IDisposable
    {
        private readonly HardwareGpuTimer _timer = new(device);
        private readonly List<PreparedDraw2D> _prepared = [];
        private IGraphicsTexture? _texture;
        private IGraphicsTextureView? _view;
        private PublicRetainedApiProbeCommands? _commands;
        private bool _initialized;
        private long _submitted;

        internal IGraphicsSubmission? Submission { get; private set; }

        internal bool Uncertain { get; private set; }

        internal int Frame { get; private set; } = -1;

        internal string InputId { get; private set; } = string.Empty;

        internal double CompletionMs { get; private set; }

        internal double RetireMs { get; private set; }

        internal double? GpuSpanMs { get; private set; }

        internal string GpuQueryStatus { get; private set; } = "not-recorded";

        internal bool TimestampSupported => _timer.Supported;

        internal bool Reallocated { get; private set; }

        internal int DrawCalls { get; private set; }

        private uint Width { get; set; }

        private uint Height { get; set; }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (Uncertain || Submission is not null)
            {
                Quarantine.Add(this);
                return;
            }

            _commands?.Dispose();
            foreach (PreparedDraw2D p in _prepared)
            {
                p.Dispose();
            }

            _prepared.Clear();

            // Submission-null and non-uncertain: query work is either unsubmitted or confirmed complete.
            _timer.Dispose();
            _view?.Dispose();
            _texture?.Dispose();
        }

        internal void ResetFrame()
        {
            Frame = -1;
            InputId = string.Empty;
            GpuSpanMs = null;
            GpuQueryStatus = "not-recorded";
        }

        internal void Resize(uint width, uint height)
        {
            if (Submission is not null || Uncertain)
            {
                throw new InvalidOperationException("Cannot resize live slot.");
            }

            Reallocated = Width != width || Height != height;
            if (!Reallocated)
            {
                return;
            }

            _view?.Dispose();
            _texture?.Dispose();
            _view = null;
            _texture = null;
            _texture = device.CreateTexture(new()
            {
                Width = width,
                Height = height,
                Format = TextureFormat.Rgba8Srgb,
                Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource,
            });
            _view = _texture.CreateView();
            Width = width;
            Height = height;
            _initialized = false;
        }

        internal async Task PrepareAsync(Renderer2D renderer, IReadOnlyList<DrawList2D> lists)
        {
            foreach (DrawList2D list in lists)
            {
                _prepared.Add(await renderer.PrepareAsync(list, new()
                {
                    View = _view!,
                    LoadOp = _prepared.Count == 0 ? AttachmentLoadOp.Clear : AttachmentLoadOp.Load,
                }));
            }
        }

        internal void Encode()
        {
            _commands = new(device.CreateCommandBuffer(new()));
            var color = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite);
            _commands.Barrier(new TextureBarrierDesc
            {
                Texture = _texture!,
                Range = new(0, 1, 0, 1),
                BeforeState = _initialized ? TextureState.ColorAttachment : TextureState.Undefined,
                AfterState = TextureState.ColorAttachment,
                Before = _initialized ? color : default,
                After = color,
            });
            _initialized = true;
            GpuSpanMs = null;
            GpuQueryStatus = _timer.Supported ? "pending" : "unsupported";
            if (_timer.Supported)
            {
                _timer.Begin(_commands.Inner);
            }

            for (int i = 0; i < _prepared.Count; i++)
            {
                if (i != 0)
                {
                    _commands.Barrier(new TextureBarrierDesc
                    {
                        Texture = _texture!,
                        Range = new(0, 1, 0, 1),
                        BeforeState = TextureState.ColorAttachment,
                        AfterState = TextureState.ColorAttachment,
                        Before = color,
                        After = color,
                    });
                }

                _prepared[i].Record(_commands);
            }

            if (_timer.Supported)
            {
                _timer.End(_commands.Inner);
            }

            _commands.Finish();
            DrawCalls = _commands.DrawCalls;
        }

        internal void Submit(int frame, string inputId)
        {
            Frame = frame;
            InputId = inputId;
            _submitted = Stopwatch.GetTimestamp();
            Uncertain = true;
            Submission = device.Queue.Submit([_commands!.Inner]);
            Uncertain = false;
        }

        internal async Task CompleteAsync()
        {
            if (Submission is null)
            {
                if (Uncertain)
                {
                    throw new InvalidOperationException("Unknown Submit acceptance.");
                }

                return;
            }

            try
            {
                await Submission.WaitAsync();
                if (Submission.Status != SubmissionStatus.Completed)
                {
                    throw new InvalidOperationException("Incomplete fence.");
                }

                CompletionMs = Ms(_submitted);

                // Read only the confirmed-completed slot pair, before command disposal or reuse.
                if (_timer.Supported)
                {
                    double span = _timer.ReadMilliseconds();
                    if (!double.IsFinite(span) || span < 0)
                    {
                        GpuQueryStatus = "invalid";
                        throw new InvalidOperationException("Invalid completed GPU timestamp span.");
                    }

                    GpuSpanMs = span;
                    GpuQueryStatus = "valid";
                }

                long t = Stopwatch.GetTimestamp();
                _commands!.Dispose();
                _commands = null;
                foreach (PreparedDraw2D draw in _prepared)
                {
                    draw.Dispose();
                }

                _prepared.Clear();
                Submission.Dispose();
                Submission = null;
                RetireMs = Ms(t);
            }
            catch
            {
                Uncertain = true;
                throw;
            }
        }

        internal async Task<byte[]> ReadAsync()
        {
            if (Submission is not null || Uncertain)
            {
                throw new InvalidOperationException("Read before completion.");
            }

            uint alignment = device.GetTextureCopyLayout(TextureFormat.Rgba8Srgb).BytesPerRowAlignment;
            uint pitch = (((Width * 4) + alignment - 1) / alignment) * alignment;
            IGraphicsBuffer<byte> buffer = device.CreateBuffer<byte>(new()
            {
                Count = (ulong)pitch * Height,
                Usage = BufferUsage.CopyDestination,
                Memory = MemoryPreference.Readback,
            });
            IGraphicsCommandBuffer cmd = device.CreateCommandBuffer(new());
            IGraphicsSubmission? copy = null;
            try
            {
                cmd.Barrier(new TextureBarrierDesc
                {
                    Texture = _texture!,
                    Range = new(0, 1, 0, 1),
                    BeforeState = TextureState.ColorAttachment,
                    AfterState = TextureState.CopySource,
                    Before = new(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite),
                    After = new(PipelineStage.Copy, ResourceAccess.CopyRead),
                });
                cmd.CopyTextureToBuffer(new() { Texture = _texture!, Width = Width, Height = Height, }, new() { Buffer = buffer.SliceElements(0, buffer.Count), BytesPerRow = pitch, RowsPerImage = Height, });
                cmd.Barrier(new BufferBarrierDesc<byte>
                {
                    Buffer = buffer.SliceElements(0, buffer.Count),
                    Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite),
                    After = new(PipelineStage.Host, ResourceAccess.HostRead),
                });
                cmd.Barrier(new TextureBarrierDesc
                {
                    Texture = _texture!,
                    Range = new(0, 1, 0, 1),
                    BeforeState = TextureState.CopySource,
                    AfterState = TextureState.ColorAttachment,
                    Before = new(PipelineStage.Copy, ResourceAccess.CopyRead),
                    After = new(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite),
                });
                cmd.Finish();
                Uncertain = true;
                copy = device.Queue.Submit([cmd]);
                await copy.WaitAsync();
                if (copy.Status != SubmissionStatus.Completed)
                {
                    throw new InvalidOperationException("Readback incomplete.");
                }

                Uncertain = false;
                await buffer.MapAsync();
                byte[] padded = new byte[checked((int)buffer.Count)];
                buffer.CopyTo(padded);
                buffer.Unmap();
                byte[] packed = new byte[checked((int)(Width * Height * 4))];
                for (int y = 0; y < Height; y++)
                {
                    Array.Copy(padded, (long)y * pitch, packed, (long)y * Width * 4, Width * 4);
                }

                return packed;
            }
            finally
            {
                if (Uncertain)
                {
                    Quarantine.AddRange(new object[]
                    {
                        device,
                        this,
                        buffer,
                        cmd,
                        copy!,
                    });
                }
                else
                {
                    copy?.Dispose();
                    cmd.Dispose();
                    buffer.Dispose();
                }
            }
        }
    }
}
