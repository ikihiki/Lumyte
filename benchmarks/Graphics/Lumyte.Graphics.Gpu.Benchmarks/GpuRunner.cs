using System.Diagnostics;
using System.Security.Cryptography;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.BenchmarkWorkloads;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Incremental.Benchmarks;
using Lumyte.Graphics.Vulkan;

namespace Lumyte.Graphics.Gpu.Benchmarks;

internal sealed class GpuRunner(VulkanDevice device, Options options)
{
    internal const int Width = 1920;
    internal const int Height = 1080;
    private readonly RectangleTrace _trace = new(options.Update, options.Locality);

    internal List<object> Checks { get; } = [];

    internal async Task<(FrameSample[] Frames, double ElapsedMs)> RunAsync()
    {
        using var fixture = new RectangleFixture(_trace, options.Granularity, options.Retained);
        var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
        PublicRetainedApiProbeFrame[] slots = Enumerable.Range(0, options.Depth).Select(_ => new PublicRetainedApiProbeFrame(device, Width, TextureFormat.Rgba8Srgb, Height)).ToArray();
        var pending = new FrameSample?[slots.Length];
        List<double>[] gpuValues = Enumerable.Range(0, slots.Length).Select(_ => new List<double>(1)).ToArray();

        // Allocate result objects and IDs before timed intervals, including warmup.
        FrameSample[] samples = Enumerable.Range(0, options.Frames + options.Warmup).Select(i => new FrameSample
        {
            Frame = i + 1,
            InputId = _trace.InputId(i + 1),
            Phase = i < options.Warmup ? "warmup" : options.Phase,
        }).ToArray();
        try
        {
            // Cold must not warm the measured renderer/pipelines. Validate after its first frame instead.
            if (options.Phase != "cold")
            {
                await CheckAsync(0);
                await CheckAsync(1);
            }

            long begin = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                if (i == options.Warmup)
                {
                    for (int s = 0; s < slots.Length; s++)
                    {
                        await CompleteAsync(s);
                    }

                    // Reset logical state outside timed windows so first measured transition is real.
                    fixture.Apply(i);
                    begin = Stopwatch.GetTimestamp();
                }

                int index = i % slots.Length;
                FrameSample sample = samples[i];
                long outer = Stopwatch.GetTimestamp();
                long wait = Stopwatch.GetTimestamp();
                await CompleteAsync(index);
                sample.SlotWaitMs = Ms(wait);
                DrawingRetentionStatistics2D before = renderer.RetentionStatistics;
                long host = Stopwatch.GetTimestamp();
                long update = Stopwatch.GetTimestamp();
                fixture.Apply(sample.Frame);
                sample.RecordPublishMs = Ms(update);
                long prepare = Stopwatch.GetTimestamp();
                if (options.Retained)
                {
                    using DrawingSceneReadLease2D lease = fixture.Scene.Borrow();
                    slots[index].Retained = await renderer.PrepareAsync(lease, slots[index].Target);
                }
                else
                {
                    slots[index].Legacy = await renderer.PrepareAsync(fixture.Recording!, slots[index].Target);
                }

                sample.PrepareMs = Ms(prepare);
                slots[index].EncodeSubmit(device);
                sample.HostFrameMs = Ms(host);
                sample.HostIncludingSlotMs = Ms(outer);
                sample.EncodeMs = slots[index].EncodeMs;
                sample.SubmitMs = slots[index].SubmitMs;
                sample.DrawCalls = slots[index].DrawCalls;
                sample.ChangedElements = fixture.ChangedElements;
                sample.ChangedNodes = fixture.ChangedNodes;
                sample.RerecordedCommands = fixture.RerecordedCommands;
                DrawingRetentionStatistics2D after = renderer.RetentionStatistics;
                sample.CompiledRuns = after.CompiledRuns - before.CompiledRuns;
                sample.ReusedRuns = after.ReusedRuns - before.ReusedRuns;
                sample.CachedAssets = after.CachedAssets;
                sample.EstimatedCachedBytes = after.CachedBytes;
                pending[index] = sample;
            }

            for (int s = 0; s < slots.Length; s++)
            {
                await CompleteAsync(s);
            }

            double elapsed = Ms(begin); // Includes final fence waits and retirement, excludes checks/output.

            // Validate actual final targets of every in-flight slot, not only a newly prepared surrogate.
            for (int s = 0; s < slots.Length; s++)
            {
                int last = samples.Length - 1 - ((samples.Length - 1 - s + slots.Length) % slots.Length);
                if (last >= 0)
                {
                    await VerifyTargetAsync(slots[s], samples[last].Frame);
                }
            }

            await CheckAsync(0);
            await CheckAsync(1);
            return (samples.Skip(options.Warmup).ToArray(), elapsed);
        }
        finally
        {
            foreach (PublicRetainedApiProbeFrame slot in slots)
            {
                slot.Dispose();
            }

            if (PublicRetainedApiProbe.QuarantinedObjects.Count == 0)
            {
                renderer.Dispose();
            }
            else
            {
                PublicRetainedApiProbe.QuarantinedObjects.Add(renderer);
            }
        }

        async Task CompleteAsync(int index)
        {
            PublicRetainedApiProbeFrame slot = slots[index];

            // Preallocated per-slot storage, consumed before query pool reuse.
            List<double> gpu = gpuValues[index];
            gpu.Clear();
            await slot.CompleteAsync(gpu);
            if (pending[index] is { } sample)
            {
                sample.CompletionLatencyMs = slot.CompletionMs;
                sample.RetirementMs = slot.DisposeMs;
                sample.GpuSpanMs = gpu.Count == 1 ? gpu[0] : null;
                pending[index] = null;
            }
        }

        async Task CheckAsync(int frame)
        {
            // Separate fixture and renderer prevent perturbing measured retained cache/state.
            using var checkFixture = new RectangleFixture(_trace, options.Granularity, options.Retained);
            using var checkRenderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
            using var target = new PublicRetainedApiProbeFrame(device, Width, TextureFormat.Rgba8Srgb, Height);
            checkFixture.Apply(frame);
            if (options.Retained)
            {
                using DrawingSceneReadLease2D lease = checkFixture.Scene.Borrow();
                target.Retained = await checkRenderer.PrepareAsync(lease, target.Target);
            }
            else
            {
                target.Legacy = await checkRenderer.PrepareAsync(checkFixture.Recording!, target.Target);
            }

            target.EncodeSubmit(device);
            await target.CompleteAsync(null);
            await VerifyTargetAsync(target, frame);
        }
    }

    private static double Ms(long start) => Stopwatch.GetElapsedTime(start).TotalMilliseconds;

    private async Task VerifyTargetAsync(PublicRetainedApiProbeFrame target, int frame)
    {
        byte[] actual = await target.ReadAsync(device);
        _trace.VerifyAnalytic(actual, frame, Width, Height);
        using var referenceRenderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
        using var reference = new PublicRetainedApiProbeFrame(device, Width, TextureFormat.Rgba8Srgb, Height);
        reference.Legacy = await referenceRenderer.PrepareAsync(_trace.Reference(frame), reference.Target);
        reference.EncodeSubmit(device);
        await reference.CompleteAsync(null);
        byte[] expected = await reference.ReadAsync(device);
        _trace.VerifyAnalytic(expected, frame, Width, Height);
        if (!expected.AsSpan().SequenceEqual(actual))
        {
            throw new InvalidOperationException("Independent exact pixel comparison failed.");
        }

        Checks.Add(new { frame, inputId = _trace.InputId(frame), status = "passed", exactRgba8 = true, analyticElements = RectangleTrace.Count, sha256 = Convert.ToHexString(SHA256.HashData(actual)) });
    }
}
