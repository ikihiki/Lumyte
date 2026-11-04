using System.Diagnostics;
using System.Security.Cryptography;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Vulkan;

namespace Lumyte.Graphics.Incremental.Benchmarks;

internal static class PublicRetainedApiProbe
{
    internal static readonly List<object> QuarantinedObjects = [];

    internal static async Task RunAsync(bool cpuOnly)
    {
        int warmup = Positive("PUBLIC_RETAINED_WARMUP", 3);
        int samples = Positive("PUBLIC_RETAINED_SAMPLES", 10);
        bool reverse = Environment.GetEnvironmentVariable("PUBLIC_RETAINED_REVERSE") == "1";
        Emit(new
        {
            scope = "public-retained-api-metadata",
            sourceRevision = Environment.GetEnvironmentVariable("DRAWING_BASELINE"),
            sourceDirty = Environment.GetEnvironmentVariable("PUBLIC_RETAINED_SOURCE_DIRTY"),
            repository = Environment.GetEnvironmentVariable("WORKLOAD_REPO_ROOT") ?? Directory.GetCurrentDirectory(),
            process = Environment.ProcessId,
            runtime = Environment.Version.ToString(),
            os = Environment.OSVersion.ToString(),
            logicalProcessors = Environment.ProcessorCount,
            cpu = Environment.GetEnvironmentVariable("PUBLIC_RETAINED_CPU"),
            configuration = "Release required by reproduction command; not runtime-detected",
            tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
            tieredPGO = Environment.GetEnvironmentVariable("DOTNET_TieredPGO"),
            serverGC = System.Runtime.GCSettings.IsServerGC,
            icd = Environment.GetEnvironmentVariable("VK_DRIVER_FILES"),
            warmup,
            samples,
            reverse,
            cpuOnly,
            window = "per-frame Stopwatch elapsed CPU wall milliseconds; async prepare may include waits, not thread CPU cycles",
            drawCalls = "actual successful IRenderEncoder Draw/DrawIndexed/DrawIndirect/DrawIndexedIndirect calls forwarded to Vulkan; not GPU work/primitive counters",
            boundaries = "inputMs: signal set or Canvas new affected-node recording + ReplaceContent; updateMs: actual UiDrawingScene2D.Update only; referenceMs: independent full Canvas generation only in legacy mode; prepareMs: borrow + awaited PrepareAsync + lease release; recordFinishMs: Frame.Record through command Finish, including timer end; encodeMs: command creation + barrier + timer begin + Record/Finish; submitMs: queue Submit + excluded AttachSubmission tail; submitCompletionMs: submit start through fence wait; retireMs: commands Dispose then frame Retire/Dispose after completed fence",
            correctness = "independent full Canvas exact byte readback before measurements for both states and after each series; excluded from all windows",
            size = "256x256 RGBA8 sRGB, identical logical coordinates and command order for every partition; UI grid varies with count but is identical across modes",
            ui10000 = "CPU update profile only; no GPU Prepare/Record or GPU pixel correctness claim",
            uiDirtyFragments = "one leaf changes; visual invalidation also re-records empty ancestors: flat panel 2 fragments, grid stacks 3; LastRecordingCount reports actual calls",
            retentionBudget = "default renderer cache budget: 512 assets; UI1000 may evict/miss, no guarantee of all-static reuse",
            cache = "public renderer counters; compiledRuns is NOT a draw call count; CachedBytes is an estimate",
            maxCachedAssets = 512,
            maxCachedBytes = 64 * 1024 * 1024,
            maxFramesInFlight = 3,
        });

        foreach (int count in Order(new[] { 100, 1000, 10000 }, reverse))
        {
            foreach (bool change in new[] { false, true })
            {
                using var inputs = new PublicRetainedApiProbeInputs(count, true, flatCpuFixture: true);
                CpuUi(inputs, change, warmup, samples);
            }
        }

        if (cpuOnly)
        {
            return;
        }

        var device = VulkanDevice.Create();
        try
        {
            using (var guard = new HardwareGpuTimer(device))
            {
                Emit(new { scope = "public-retained-api-device", device = guard.DeviceDescription, timestampSupported = guard.Supported });
            }

            foreach (int partitions in Order(new[] { 1, 16, 256 }, reverse))
            {
                foreach (bool change in new[] { false, true })
                {
                    foreach (bool retained in reverse ? new[] { true, false } : new[] { false, true })
                    {
                        using var inputs = new PublicRetainedApiProbeInputs(partitions);
                        await GpuCaseAsync(device, inputs, change, retained, warmup, samples);
                    }
                }
            }

            foreach (int count in Order(new[] { 100, 1000 }, reverse))
            {
                foreach (bool change in new[] { false, true })
                {
                    foreach (bool retained in reverse ? new[] { true, false } : new[] { false, true })
                    {
                        using var inputs = new PublicRetainedApiProbeInputs(count, true);
                        await GpuCaseAsync(device, inputs, change, retained, warmup, samples);
                    }
                }
            }
        }
        finally
        {
            if (QuarantinedObjects.Count == 0)
            {
                device.Dispose();
            }
            else
            {
                QuarantinedObjects.Add(device);
            }
        }
    }

    private static double Ms(long start) => Stopwatch.GetElapsedTime(start).TotalMilliseconds;

    private static void Emit(object row) => ProductionRetainedProbe.Emit(row);

    private static void CpuUi(PublicRetainedApiProbeInputs inputs, bool change, int warmup, int samples)
    {
        var rows = new List<object>(samples);
        for (int frame = -warmup; frame < samples; frame++)
        {
            long begin = Stopwatch.GetTimestamp();
            if (change)
            {
                inputs.ChangeInput();
            }

            double inputMs = Ms(begin);
            begin = Stopwatch.GetTimestamp();
            inputs.Update();
            double updateMs = Ms(begin);
            VerifyRecordings(inputs, change);
            if (frame >= 0)
            {
                rows.Add(new
                {
                    scope = "public-retained-ui-cpu-frame",
                    frame,
                    rectangleCount = inputs.RectangleCount,
                    uiElementCount = inputs.UiElementCount,
                    layoutKind = inputs.LayoutKind,
                    state = change ? "one-leaf-fill-change" : "static",
                    inputMs,
                    updateMs,
                    lastRecordingCount = inputs.LastRecordingCount,
                    gpuMeasured = false,
                });
            }
        }

        foreach (object row in rows)
        {
            Emit(row);
        }
    }

    private static async Task GpuCaseAsync(VulkanDevice device, PublicRetainedApiProbeInputs inputs, bool change, bool retained, int warmup, int samples)
    {
        Renderer2D renderer = ProductionRetainedProbe.NewRenderer(device);
        var target = new PublicRetainedApiProbeFrame(device, 256, TextureFormat.Rgba8Srgb);
        try
        {
            // Check both logical states independently, and prove the leaf change
            // actually affects visible pixels (not merely a revision counter).
            byte[] first = await CheckPixelsAsync(device, inputs, renderer, target);
            inputs.ChangeInput();
            inputs.Update();
            byte[] second = await CheckPixelsAsync(device, inputs, renderer, target);
            if (first.AsSpan().SequenceEqual(second))
            {
                throw new InvalidOperationException("The requested actual command change produced no visible pixel change.");
            }

            inputs.ChangeInput();
            inputs.Update();
            var rows = new List<object>(samples);
            var gpu = new List<double>(samples);
            for (int frame = -warmup; frame < samples; frame++)
            {
                DrawingRetentionStatistics2D before = renderer.RetentionStatistics;
                long begin = Stopwatch.GetTimestamp();
                if (change)
                {
                    inputs.ChangeInput();
                }

                double inputMs = Ms(begin);
                begin = Stopwatch.GetTimestamp();
                inputs.Update();
                double updateMs = Ms(begin);
                VerifyRecordings(inputs, change);
                begin = Stopwatch.GetTimestamp();
                DrawList2D? reference = retained ? null : inputs.Reference();
                double referenceMs = Ms(begin);
                begin = Stopwatch.GetTimestamp();
                if (retained)
                {
                    using DrawingSceneReadLease2D lease = inputs.Scene.Borrow();
                    target.Retained = await renderer.PrepareAsync(lease, target.Target);
                }
                else
                {
                    target.Legacy = await renderer.PrepareAsync(reference!, target.Target);
                }

                double prepareMs = Ms(begin);
                target.EncodeSubmit(device);
                await target.CompleteAsync(frame >= 0 ? gpu : null);
                DrawingRetentionStatistics2D after = renderer.RetentionStatistics;
                if (frame >= 0)
                {
                    rows.Add(new
                    {
                        scope = "public-retained-api-gpu-frame",
                        frame,
                        fixture = inputs.IsUi ? "actual-ui-adapter" : "constant-command-partitions",
                        layoutKind = inputs.LayoutKind,
                        rectangleCount = inputs.RectangleCount,
                        commandCount = inputs.RectangleCount,
                        contentNodeCount = inputs.ContentNodeCount,
                        sceneNodeCount = inputs.SceneNodeCount,
                        uiElementCount = inputs.UiElementCount,
                        mode = retained ? "public-retained" : "independent-canvas",
                        state = change ? "one-command-change" : "static",
                        inputMs,
                        updateMs,
                        referenceMs,
                        prepareMs,
                        recordFinishMs = target.RecordFinishMs,
                        encodeMs = target.EncodeMs,
                        submitMs = target.SubmitMs,
                        submitCompletionMs = target.CompletionMs,
                        retireMs = target.DisposeMs,
                        actualIssuedDrawCalls = target.DrawCalls,
                        lastRecordingCount = inputs.LastRecordingCount,
                        compiledRuns = after.CompiledRuns - before.CompiledRuns,
                        reusedRuns = after.ReusedRuns - before.ReusedRuns,
                        cachedAssets = after.CachedAssets,
                        estimatedCachedBytes = after.CachedBytes,
                        gpuRecordedSpanMs = target.Timer.Supported ? (double?)gpu[^1] : null,
                    });
                }
            }

            byte[] actual = await target.ReadAsync(device);
            byte[] expected = await ReferencePixelsAsync(device, inputs);
            ProductionRetainedProbe.Equal(expected, actual, Name(inputs) + "/final/" + retained + "/" + change);
            foreach (object row in rows)
            {
                Emit(row);
            }

            Emit(new { scope = "public-retained-api-series-end", fixture = Name(inputs), retained, change, warmup, samples, sha256 = Convert.ToHexString(SHA256.HashData(actual)) });
        }
        finally
        {
            if (target.UnknownSubmission)
            {
                QuarantinedObjects.AddRange(new object[] { renderer, target });
            }
            else
            {
                await target.CompleteAsync(null);
                target.Dispose();
                renderer.Dispose();
            }
        }
    }

    private static async Task<byte[]> CheckPixelsAsync(VulkanDevice device, PublicRetainedApiProbeInputs inputs, Renderer2D renderer, PublicRetainedApiProbeFrame target)
    {
        using (DrawingSceneReadLease2D lease = inputs.Scene.Borrow())
        {
            target.Retained = await renderer.PrepareAsync(lease, target.Target);
        }

        target.EncodeSubmit(device);
        await target.CompleteAsync(null);
        byte[] actual = await target.ReadAsync(device);
        ProductionRetainedProbe.Equal(await ReferencePixelsAsync(device, inputs), actual, Name(inputs) + "/preflight");
        return actual;
    }

    private static async Task<byte[]> ReferencePixelsAsync(VulkanDevice device, PublicRetainedApiProbeInputs inputs)
    {
        Renderer2D renderer = ProductionRetainedProbe.NewRenderer(device);
        var reference = new PublicRetainedApiProbeFrame(device, 256, TextureFormat.Rgba8Srgb);
        try
        {
            DrawList2D content = inputs.Reference();
            if (content.Count != inputs.RectangleCount)
            {
                throw new InvalidOperationException("Independent Canvas rectangle command count differs.");
            }

            reference.Legacy = await renderer.PrepareAsync(content, reference.Target);
            reference.EncodeSubmit(device);
            await reference.CompleteAsync(null);
            return await reference.ReadAsync(device);
        }
        finally
        {
            if (reference.UnknownSubmission)
            {
                QuarantinedObjects.AddRange(new object[] { renderer, reference });
            }
            else
            {
                await reference.CompleteAsync(null);
                reference.Dispose();
                renderer.Dispose();
            }
        }
    }

    private static string Name(PublicRetainedApiProbeInputs inputs) => inputs.IsUi ? "ui/" + inputs.RectangleCount : "partitions/" + inputs.ContentNodeCount;

    private static IEnumerable<int> Order(int[] values, bool reverse) => reverse ? values.Reverse() : values;

    private static void VerifyRecordings(PublicRetainedApiProbeInputs inputs, bool change)
    {
        if (inputs.IsUi && inputs.LastRecordingCount != (change ? inputs.ExpectedChangedRecordings : 0))
        {
            throw new InvalidOperationException("Expected zero static or the changed leaf plus dirty ancestor fragments; got " + inputs.LastRecordingCount);
        }
    }

    private static int Positive(string key, int fallback)
    {
        string? value = Environment.GetEnvironmentVariable(key);
        if (value is null)
        {
            return fallback;
        }

        return int.TryParse(value, out int count) && count is >= 1 and <= 10000
            ? count : throw new ArgumentException(key + " must be 1..10000.");
    }
}
