using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Vulkan;

namespace Lumyte.Graphics.Incremental.Benchmarks;

internal static class ProductionRetainedProbe
{
    private static readonly List<object> _quarantinedObjects = [];

    internal static async Task RunAsync(bool checksOnly)
    {
        var device = VulkanDevice.Create();
        try
        {
            await RunDeviceAsync(device, checksOnly);
        }
        finally
        {
            if (_quarantinedObjects.Count == 0)
            {
                device.Dispose();
            }
            else
            {
                _quarantinedObjects.Add(device); // Never destroy the device under uncertain work.
            }
        }
    }

    internal static Renderer2D NewRenderer(VulkanDevice device) => new(device, DrawingCommandRenderers2D.CreateDefault(), retentionOptions: new() { MaxFramesInFlight = 3 });

    internal static void Equal(byte[] expected, byte[] actual, string name)
    {
        if (!expected.AsSpan().SequenceEqual(actual))
        {
            throw new InvalidOperationException("Exact production retained pixels differ: " + name + "; expected=" +
                Convert.ToHexString(SHA256.HashData(expected)) + "; actual=" + Convert.ToHexString(SHA256.HashData(actual)));
        }

        Emit(new
        {
            check = "production-retained-exact-pixels",
            name,
            bytes = actual.Length,
            sha256 = Convert.ToHexString(SHA256.HashData(actual)),
            status = "passed",
        });
    }

    internal static void Emit(object value) => Console.WriteLine(JsonSerializer.Serialize(value));

    private static async Task RunDeviceAsync(VulkanDevice device, bool checksOnly)
    {
        // HardwareGpuTimer validates the ACTUAL device, not just an environment label.
        using (var identity = new HardwareGpuTimer(device))
        {
            Emit(new
            {
                scope = "production-retained-metadata",
                device = identity.DeviceDescription,
                timestampSupported = identity.Supported,
                runtime = Environment.Version.ToString(),
                os = Environment.OSVersion.ToString(),
                cpu = Environment.ProcessorCount,
                sourceRevision = Environment.GetEnvironmentVariable("DRAWING_BASELINE"),
                tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
                tieredPGO = Environment.GetEnvironmentVariable("DOTNET_TieredPGO"),
                serverGC = System.Runtime.GCSettings.IsServerGC,
                icd = Environment.GetEnvironmentVariable("VK_DRIVER_FILES"),
                coldSetup = "outside measured window",
                allocation = "process-wide GC.GetTotalAllocatedBytes; host sample-object allocations included",
                rectangleCount = "count is total rectangles: two shared-content placements, count/2 source rectangles each",
                inputFairness = "both modes perform identical scene/input updates; legacy additionally records/reuses the reference Canvas",
                gpuSpan = "TOP_OF_PIPE to BOTTOM_OF_PIPE; clear/render included; initial target barrier excluded; not exclusive busy time",
                retentionStatistics = "renderer-issued counters; CachedBytes estimated idle private bytes, not driver memory",
                failureInjection = "unverified; unknown Submit acceptance deliberately quarantines resources",
                uiAdaptation = "one content placement per existing fixture layer; full 1280x720 viewport; not UiDrawingScene2D adapter timing",
                targetSizes = "mixed pixel checks 64x64 both formats; rectangle timing 256x256; real UI 1280x720",
                serialStageContract = "updateAndInputRecording includes generator/layout/scene edits; referenceRecording is cached/replayed Canvas only; encode includes command allocation/barrier/Record/Finish; submitCompletion is submit-start to completed wait; dispose is confirmed-complete frame retirement",
                pipelineStageContract = "pipeline-host-frame ends after submit; slotWaitAndRetire is slot safety only; final drain included in throughput; gpu array is retirement order",
                comparisonContract = "exact final logical frame across both modes and depths; no AA changes or pixel tolerance; unsupported commands throw",
            });
        }

        await ProductionRetainedChecks.RunAsync(device);
        await ProductionRetainedUiChecks.RunAsync(device);
        if (!checksOnly)
        {
            await RunQuickComparisonsAsync(device);
        }

        int warmup = checksOnly ? 3 : PositiveEnvironment("RETAINED_WARMUP", 21);
        int samples = checksOnly ? 4 : PositiveEnvironment("RETAINED_SAMPLES", 40);
        string[] patterns = ["rect-static", "rect-page", "rect-whole", "rect-group-move", "rect-order"];
        (string Name, int Count)[] cases = (checksOnly ? new[] { 258 } : new[] { 1024, 10240 })
            .SelectMany(count => patterns.Select(name => (Name: name, Count: count)))
            .Concat(new[] { "game-hud-static-overlay", "game-hud-values", "editor-idle", "editor-line-edit" }
                .Select(name => (Name: name, Count: 64))).ToArray();
        string? filter = Environment.GetEnvironmentVariable("RETAINED_CASES");
        if (!checksOnly && !string.IsNullOrWhiteSpace(filter))
        {
            string[] names = filter.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (names.Any(n => !cases.Any(c => c.Name == n)))
            {
                throw new ArgumentException("Unknown RETAINED_CASES name.");
            }

            cases = cases.Where(c => names.Contains(c.Name)).ToArray();
        }

        foreach ((string Name, int Count) item in cases)
        {
            await CompareAsync(device, item.Name, item.Count, warmup, samples);
        }
    }

    private static async Task RunQuickComparisonsAsync(VulkanDevice device)
    {
        // RETAINED_CASES bounds timing only; mandatory correctness never disappears.
        foreach (string name in new[]
        {
            "rect-static", "rect-page", "rect-whole", "rect-group-move", "rect-order",
            "game-hud-static-overlay", "game-hud-values", "editor-idle", "editor-line-edit",
        })
        {
            await CompareAsync(device, name, name.StartsWith("rect-", StringComparison.Ordinal) ? 258 : 64, 3, 4);
        }
    }

    private static async Task CompareAsync(VulkanDevice device, string name, int count, int warmup, int samples)
    {
        byte[]? expected = null;
        foreach (int depth in new[] { 1, 3 })
        {
            foreach (bool retained in Environment.GetEnvironmentVariable("RETAINED_REVERSE") == "1" ? new[] { true, false } : new[] { false, true })
            {
                byte[] pixels;
                try
                {
                    pixels = await RunCaseAsync(device, name, count, retained, depth, warmup, samples);
                }
                catch (NotSupportedException error)
                {
                    throw new NotSupportedException(name + "/" + count + "/depth" + depth + ": " + error.Message, error);
                }

                if (expected is null)
                {
                    expected = pixels;
                }
                else
                {
                    Equal(expected, pixels, name + "/" + count + "/depth" + depth);
                }
            }
        }
    }

    private static int PositiveEnvironment(string key, int fallback)
    {
        string? value = Environment.GetEnvironmentVariable(key);
        if (value is null)
        {
            return fallback;
        }

        if (!int.TryParse(value, out int result) || result < 1 || result > 10000)
        {
            throw new ArgumentException(key + " must be 1..10000.");
        }

        return result;
    }

    private static async Task<byte[]> RunCaseAsync(VulkanDevice device, string name, int count, bool retained, int depth, int warmup, int samples)
    {
        using var inputs = new ProductionRetainedInputs(name, count);
        Renderer2D renderer = NewRenderer(device);
        bool rectangles = name.StartsWith("rect-", StringComparison.Ordinal);
        uint width = rectangles ? 256u : 1280u, height = rectangles ? 256u : 720u;
        Slot[] slots = Enumerable.Range(0, depth).Select(_ => new Slot(device, width, TextureFormat.Rgba8Srgb, height)).ToArray();
        var rows = new List<object>(samples);
        var gpu = new List<double>(samples);
        try
        {
            for (int i = 0; i < warmup; i++)
            {
                Slot slot = slots[i % depth];
                await slot.CompleteAsync(null);
                inputs.Advance();
                await PrepareAsync(renderer, slot, inputs, retained);
                slot.EncodeSubmit(device);
            }

            foreach (Slot slot in slots)
            {
                await slot.CompleteAsync(null); // reads/discards every warm query
            }

            DrawingRetentionStatistics2D before = renderer.RetentionStatistics;
            long allocated = GC.GetTotalAllocatedBytes(false), begin = Stopwatch.GetTimestamp();
            for (int i = 0; i < samples; i++)
            {
                long frameAllocated = GC.GetTotalAllocatedBytes(false);
                long frame = Stopwatch.GetTimestamp(), stage = frame;
                Slot slot = slots[i % depth];
                await slot.CompleteAsync(gpu);
                double slotWaitAndRetire = Ms(stage);
                stage = Stopwatch.GetTimestamp();
                inputs.Advance();
                double updateAndInputRecording = Ms(stage);
                stage = Stopwatch.GetTimestamp();
                DrawList2D? reference = retained ? null : inputs.Reference();
                double referenceRecording = Ms(stage);
                stage = Stopwatch.GetTimestamp();
                await PrepareAsync(renderer, slot, inputs, retained, reference);
                double prepare = Ms(stage);
                stage = Stopwatch.GetTimestamp();
                slot.EncodeSubmit(device);
                double encodeAndSubmit = Ms(stage);
                if (depth == 1)
                {
                    await slot.CompleteAsync(gpu);
                }

                rows.Add(new
                {
                    scope = depth == 1 ? "serial-frame" : "pipeline-host-frame",
                    name,
                    count,
                    mode = retained ? "production-retained" : "legacy-canvas",
                    depth,
                    iteration = i,
                    slotWaitAndRetire,
                    updateAndInputRecording,
                    referenceRecording,
                    prepare,
                    encode = slot.EncodeMs,
                    submit = slot.SubmitMs,
                    encodeAndSubmit,
                    submitCompletion = depth == 1 ? (double?)slot.CompletionMs : null,
                    dispose = depth == 1 ? (double?)slot.DisposeMs : null,
                    frameWall = Ms(frame),
                    allocatedProcessWide = GC.GetTotalAllocatedBytes(false) - frameAllocated,
                });
            }

            foreach (Slot slot in slots)
            {
                await slot.CompleteAsync(gpu);
            }

            double elapsed = Ms(begin);
            long bytes = GC.GetTotalAllocatedBytes(false) - allocated;
            DrawingRetentionStatistics2D after = renderer.RetentionStatistics;
            if (slots[0].Timer.Supported && gpu.Count != samples)
            {
                throw new InvalidOperationException("Missing GPU timestamp samples.");
            }

            byte[] pixels = await slots[(samples - 1) % depth].ReadAsync(device);
            inputs.Verify();
            long disposeStart = Stopwatch.GetTimestamp();
            renderer.Dispose();
            double cacheOwnerDisposeOutsideWindow = Ms(disposeStart);
            foreach (object row in rows)
            {
                Emit(row);
            }

            Emit(new
            {
                scope = "production-retained-throughput",
                name,
                count,
                depth,
                mode = retained ? "production-retained" : "legacy-canvas",
                frames = samples,
                warmup,
                width,
                height,
                format = TextureFormat.Rgba8Srgb.ToString(),
                elapsedMs = elapsed,
                headlessFramesPerSecond = samples * 1000 / elapsed,
                allocatedProcessWide = bytes,
                gpuRecordedSpanMs = gpu,
                includesFinalDrain = true,
                includesInputRecording = true,
                includesFrameRetirement = true,
                presentation = false,
                cacheOwnerDisposeOutsideWindow,
                compiledRuns = after.CompiledRuns - before.CompiledRuns,
                reusedRuns = after.ReusedRuns - before.ReusedRuns,
                idleAssets = after.CachedAssets,
                estimatedIdleBytes = after.CachedBytes,
                sha256 = Convert.ToHexString(SHA256.HashData(pixels)),
            });
            return pixels;
        }
        finally
        {
            // No abort-after-Submit: any unknown acceptance leaves its slot/device
            // alive until process exit rather than destroying potentially live GPU objects.
            if (slots.Any(s => s.UnknownSubmission))
            {
                _quarantinedObjects.AddRange(slots);
                _quarantinedObjects.Add(renderer);
                throw new InvalidOperationException("Unknown submission acceptance: resources quarantined; terminate this benchmark process.");
            }

            foreach (Slot slot in slots)
            {
                await slot.CompleteAsync(null);
                slot.Dispose();
            }

            renderer.Dispose();
        }
    }

    private static async Task PrepareAsync(Renderer2D renderer, Slot slot, ProductionRetainedInputs inputs, bool retained, DrawList2D? reference = null)
    {
        if (retained)
        {
            using DrawingSceneReadLease2D lease = inputs.Scene.Borrow();
            slot.Retained = await renderer.PrepareAsync(lease, slot.Target);
        } // Release the CPU borrow BEFORE encode/submit and the next CPU update.
        else
        {
            slot.Legacy = await renderer.PrepareAsync(reference ?? inputs.Reference(), slot.Target);
        }
    }

    private static double Ms(long start) => Stopwatch.GetElapsedTime(start).TotalMilliseconds;

    internal sealed class Slot : IDisposable
    {
        private readonly uint _size;
        private readonly uint _height;
        private readonly uint _pixelBytes;
        private long _submittedAt;
        private bool _initialized;
        private bool _recorded;
        private bool _submitAttempted;
        private bool _quarantined;

        internal Slot(VulkanDevice device, uint size, TextureFormat format, uint? height = null)
        {
            _size = size;
            _height = height ?? size;
            _pixelBytes = format == TextureFormat.Rgba16Float ? 8u : 4u;
            Texture = device.CreateTexture(new()
            {
                Width = size,
                Height = _height,
                Format = format,
                Usage = TextureUsage.RenderAttachment | TextureUsage.CopySource,
            });
            View = Texture.CreateView();
            Timer = new(device);
        }

        internal IGraphicsTexture Texture { get; }

        internal IGraphicsTextureView View { get; }

        internal HardwareGpuTimer Timer { get; }

        internal RenderTarget2D Target => new() { View = View, LoadOp = AttachmentLoadOp.Clear };

        internal PreparedDrawingFrame2D? Retained { get; set; }

        internal PreparedDraw2D? Legacy { get; set; }

        internal bool UnknownSubmission => _quarantined || (_submitAttempted && Submission is null);

        internal double EncodeMs { get; private set; }

        internal double SubmitMs { get; private set; }

        internal double CompletionMs { get; private set; }

        internal double DisposeMs { get; private set; }

        private IGraphicsCommandBuffer? Commands { get; set; }

        private IGraphicsSubmission? Submission { get; set; }

        public void Dispose()
        {
            if (Submission is not null || UnknownSubmission)
            {
                _quarantinedObjects.Add(this); // Keep commands, targets, queries and frame owners alive.
                return;
            }

            Commands?.Dispose();
            if (_recorded)
            {
                Retained?.AbortUnsubmitted();
            }
            else
            {
                Retained?.Dispose();
            }

            Legacy?.Dispose();
            Timer.Dispose();
            View.Dispose();
            Texture.Dispose();
        }

        internal void EncodeSubmit(VulkanDevice device)
        {
            long start = Stopwatch.GetTimestamp();
            Commands = device.CreateCommandBuffer(new());
            var color = new BarrierScope(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite);
            Commands.Barrier(new TextureBarrierDesc
            {
                Texture = Texture,
                Range = new(0, 1, 0, 1),
                BeforeState = _initialized ? TextureState.ColorAttachment : TextureState.Undefined,
                AfterState = TextureState.ColorAttachment,
                Before = _initialized ? color : default,
                After = color,
            });
            _initialized = true;
            if (Timer.Supported)
            {
                Timer.Begin(Commands);
            }

            _recorded = true;
            if (Retained is not null)
            {
                Retained.Record(Commands);
            }
            else
            {
                Legacy!.Record(Commands);
            }

            if (Timer.Supported)
            {
                Timer.End(Commands);
            }

            Commands.Finish();
            EncodeMs = Ms(start);
            _submittedAt = Stopwatch.GetTimestamp();
            _submitAttempted = true;
            Submission = device.Queue.Submit([Commands]);
            SubmitMs = Ms(_submittedAt);
            Retained?.AttachSubmission(Submission);
        }

        internal async Task CompleteAsync(List<double>? gpu)
        {
            if (Submission is null)
            {
                return;
            }

            try
            {
                await Submission.WaitAsync();
            }
            catch
            {
                _quarantined = true;
                _quarantinedObjects.Add(this);
                throw;
            }

            CompletionMs = Ms(_submittedAt);
            if (Submission.Status != SubmissionStatus.Completed)
            {
                _quarantined = true;
                _quarantinedObjects.Add(this);
                throw new InvalidOperationException("Fence not completed.");
            }

            if (Timer.Supported)
            {
                double span = Timer.ReadMilliseconds();
                if (!double.IsFinite(span) || span < 0)
                {
                    throw new InvalidOperationException("Invalid GPU timestamp.");
                }

                gpu?.Add(span);
            }

            long start = Stopwatch.GetTimestamp();
            try
            {
                Commands!.Dispose();
                Retained?.Retire();
                Legacy?.Dispose();
                Submission.Dispose();
                Retained?.Dispose();
            }
            catch
            {
                _quarantined = true;
                _quarantinedObjects.Add(this);
                throw;
            }

            Commands = null;
            Submission = null;
            Retained = null;
            Legacy = null;
            _recorded = false;
            _submitAttempted = false;
            DisposeMs = Ms(start);
        }

        internal async Task<byte[]> ReadAsync(VulkanDevice device)
        {
            if (Submission is not null || UnknownSubmission)
            {
                throw new InvalidOperationException("Read requires final completed fence.");
            }

            uint pitch = _size * _pixelBytes;
            IGraphicsBuffer<byte> readback = device.CreateBuffer<byte>(new()
            {
                Count = (ulong)pitch * _height,
                Usage = BufferUsage.CopyDestination,
                Memory = MemoryPreference.Readback,
            });
            IGraphicsCommandBuffer copy = device.CreateCommandBuffer(new());
            copy.Barrier(new TextureBarrierDesc
            {
                Texture = Texture,
                Range = new(0, 1, 0, 1),
                BeforeState = TextureState.ColorAttachment,
                AfterState = TextureState.CopySource,
                Before = new(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite),
                After = new(PipelineStage.Copy, ResourceAccess.CopyRead),
            });
            copy.CopyTextureToBuffer(
                new() { Texture = Texture, Width = _size, Height = _height },
                new() { Buffer = readback.SliceElements(0, readback.Count), BytesPerRow = pitch, RowsPerImage = _height });
            copy.Barrier(new BufferBarrierDesc<byte>
            {
                Buffer = readback.SliceElements(0, readback.Count),
                Before = new(PipelineStage.Copy, ResourceAccess.CopyWrite),
                After = new(PipelineStage.Host, ResourceAccess.HostRead),
            });

            // Restore attachment state so repeated correctness checks can reuse this target.
            copy.Barrier(new TextureBarrierDesc
            {
                Texture = Texture,
                Range = new(0, 1, 0, 1),
                BeforeState = TextureState.CopySource,
                AfterState = TextureState.ColorAttachment,
                Before = new(PipelineStage.Copy, ResourceAccess.CopyRead),
                After = new(PipelineStage.ColorOutput, ResourceAccess.ColorRead | ResourceAccess.ColorWrite),
            });
            copy.Finish();
            IGraphicsSubmission submission;
            try
            {
                submission = device.Queue.Submit([copy]);
            }
            catch
            {
                _quarantined = true;
                _quarantinedObjects.AddRange(new object[] { this, copy, readback });
                throw;
            }

            try
            {
                await submission.WaitAsync();
            }
            catch
            {
                _quarantined = true;
                _quarantinedObjects.AddRange(new object[] { this, copy, readback, submission });
                throw;
            }

            using (submission) using (copy) using (readback)
            {
                await readback.MapAsync();
                byte[] pixels = new byte[checked((int)readback.Count)];
                readback.CopyTo(pixels);
                readback.Unmap();
                return pixels;
            }
        }
    }
}
