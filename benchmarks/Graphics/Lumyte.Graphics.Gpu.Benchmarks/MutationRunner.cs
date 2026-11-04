using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.BenchmarkWorkloads;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Incremental.Benchmarks;
using Lumyte.Graphics.Vulkan;

namespace Lumyte.Graphics.Gpu.Benchmarks;

internal static class MutationRunner
{
    internal static async Task<int> RunAsync(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        string[] allowed = ["scenario", "placement", "sharing", "depth", "frames", "output"];
        for (int i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || !args[i].StartsWith("--", StringComparison.Ordinal) || !allowed.Contains(args[i][2..]) || !values.TryAdd(args[i][2..], args[i + 1]))
            {
                throw new ArgumentException("Unknown, duplicate or incomplete mutation option.");
            }
        }

        string scenario = values.GetValueOrDefault("scenario", "D03");
        string placement = values.GetValueOrDefault("placement", "parent");
        string sharing = values.GetValueOrDefault("sharing", "shared");
        int depth = int.Parse(values.GetValueOrDefault("depth", "1"), System.Globalization.CultureInfo.InvariantCulture);
        int frames = int.Parse(values.GetValueOrDefault("frames", "8"), System.Globalization.CultureInfo.InvariantCulture);
        if (scenario is not ("D03" or "D11") || placement is not ("parent" or "individual") || sharing is not ("shared" or "distinct") || depth is not (1 or 3) || frames is < 4 or > 100000)
        {
            throw new ArgumentException("Invalid mutation profile; D03/D11, parent/individual, shared/distinct, depth1/3, frames4..100000.");
        }

        if (scenario == "D11" && (values.ContainsKey("placement") || values.ContainsKey("sharing")))
        {
            throw new ArgumentException("Placement/sharing are D03-only parameters.");
        }

        string output = values.GetValueOrDefault("output", "artifacts/graphics-benchmarks/" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        string runId = Guid.NewGuid().ToString("N");
        VulkanDevice? device = null;
        var checks = new List<object>();
        try
        {
            device = VulkanDevice.Create();
            using var identity = new HardwareGpuTimer(device);
            var manifest = new
            {
                schema = "graphics-mutation-v1",
                runId,
                scenario,
                placement,
                sharing,
                depth,
                frames,
                device = identity.DeviceDescription,
                provenance = Provenance.Capture(),
                scope = "diagnostic stages + bounded headless throughput; no warmup/pilot/formal campaign yet",
                target = "1920x1080 Rgba8Srgb DPR1; default AA/blend; no presentation",
                cache = "product defaults 512 idle assets/64MiB estimated; no snapshot retention",
                trace = scenario == "D03" ? "256 placements; translation0/4px; parent versus256 individual property edits; identical content/painter order; shared vs distinct recordings" : "4-phase repeat: insert yellow, remove yellow + reorder red, reparent blue to second group, return blue + restore red order; bounded3/4 elements",
                counters = "actual successful public Scene edit calls, forwarded API draw calls, public renderer cache stats; no inferred upload/eviction/native VRAM counters",
            };
            File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(manifest, json));

            // Full trace preflight checks initial state and each mutation phase, outside timing.
            await ExecuteAsync(4, 1, true);
            (MutationSample[] Rows, double Elapsed) measured = await ExecuteAsync(frames, depth, false);
            File.WriteAllLines(Path.Combine(output, "frames.jsonl"), measured.Rows.Select(row => JsonSerializer.Serialize(new { runId, scenario, placement, sharing, depth, row }, json)));
            File.WriteAllLines(Path.Combine(output, "checks.jsonl"), checks.Select(check => JsonSerializer.Serialize(new { runId, check }, json)));
            File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new { runId, status = "passed", performanceUse = "diagnostic only; no formal warmup or independent-process comparison", finalDrainIncluded = true, elapsedMs = measured.Elapsed, headlessFramesPerSecond = frames * 1000 / measured.Elapsed, frames }, json));
            return 0;
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new { runId, status = "failed", comparisonValid = false, error = error.ToString() }, json));
            throw;
        }
        finally
        {
            if (PublicRetainedApiProbe.QuarantinedObjects.Count == 0)
            {
                device?.Dispose();
            }
            else if (device is not null)
            {
                PublicRetainedApiProbe.QuarantinedObjects.Add(device);
            }
        }

        async Task<(MutationSample[] Rows, double Elapsed)> ExecuteAsync(int count, int slotCount, bool preflight)
        {
            using var fixture = new SceneMutationFixture(scenario, placement, sharing == "shared");
            var renderer = new Renderer2D(device!, DrawingCommandRenderers2D.CreateDefault());
            PublicRetainedApiProbeFrame[] slots = Enumerable.Range(0, slotCount).Select(_ => new PublicRetainedApiProbeFrame(device!, 1920, TextureFormat.Rgba8Srgb, 1080)).ToArray();
            var pending = new MutationSample?[slotCount];
            List<double>[] gpu = Enumerable.Range(0, slotCount).Select(_ => new List<double>(1)).ToArray();
            MutationSample[] rows = Enumerable.Range(1, count).Select(frame => new MutationSample { Frame = frame, InputId = SceneMutationFixture.InputId(scenario, frame) }).ToArray();
            try
            {
                if (preflight)
                {
                    using DrawingSceneReadLease2D lease = fixture.Scene.Borrow();
                    slots[0].Retained = await renderer.PrepareAsync(lease, slots[0].Target);
                }

                if (preflight)
                {
                    slots[0].EncodeSubmit(device!);
                    await slots[0].CompleteAsync(null);
                    await CheckAsync(slots[0], 0);
                }

                long begin = Stopwatch.GetTimestamp();
                foreach (MutationSample row in rows)
                {
                    int index = (row.Frame - 1) % slotCount;
                    long wait = Stopwatch.GetTimestamp();
                    await CompleteAsync(index);
                    row.SlotWaitMs = Ms(wait);
                    DrawingRetentionStatistics2D before = renderer.RetentionStatistics;
                    long host = Stopwatch.GetTimestamp();
                    long update = Stopwatch.GetTimestamp();
                    fixture.Apply(row.Frame);
                    row.UpdateMs = Ms(update);
                    long prepare = Stopwatch.GetTimestamp();
                    using (DrawingSceneReadLease2D lease = fixture.Scene.Borrow())
                    {
                        slots[index].Retained = await renderer.PrepareAsync(lease, slots[index].Target);
                    }

                    row.PrepareMs = Ms(prepare);
                    slots[index].EncodeSubmit(device!);
                    row.HostFrameMs = Ms(host);
                    row.EncodeMs = slots[index].EncodeMs;
                    row.SubmitMs = slots[index].SubmitMs;
                    row.DrawCalls = slots[index].DrawCalls;
                    row.PropertyEdits = fixture.PropertyEdits;
                    row.StructuralEdits = fixture.StructuralEdits;
                    row.Elements = fixture.ElementCount;
                    DrawingRetentionStatistics2D after = renderer.RetentionStatistics;
                    row.CompiledRuns = after.CompiledRuns - before.CompiledRuns;
                    row.ReusedRuns = after.ReusedRuns - before.ReusedRuns;
                    row.CachedAssets = after.CachedAssets;
                    row.EstimatedCachedBytes = after.CachedBytes;
                    pending[index] = row;
                    if (preflight)
                    {
                        await CompleteAsync(index);
                        await CheckAsync(slots[index], row.Frame);
                    }
                }

                for (int i = 0; i < slotCount; i++)
                {
                    await CompleteAsync(i);
                }

                double elapsed = Ms(begin);
                if (!preflight)
                {
                    for (int i = 0; i < slotCount; i++)
                    {
                        int lastFrame = count - ((count - 1 - i + slotCount) % slotCount);
                        await CheckAsync(slots[i], lastFrame);
                    }
                }

                return (rows, elapsed);
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
                gpu[index].Clear();
                await slots[index].CompleteAsync(gpu[index]);
                if (pending[index] is { } row)
                {
                    row.CompletionLatencyMs = slots[index].CompletionMs;
                    row.RetirementMs = slots[index].DisposeMs;
                    row.GpuSpanMs = gpu[index].Count == 1 ? gpu[index][0] : null;
                    pending[index] = null;
                }
            }
        }

        async Task CheckAsync(PublicRetainedApiProbeFrame target, int frame)
        {
            byte[] actual = await target.ReadAsync(device!);
            SceneMutationFixture.VerifyAnalytic(actual, scenario, frame);
            var renderer = new Renderer2D(device!, DrawingCommandRenderers2D.CreateDefault());
            using var reference = new PublicRetainedApiProbeFrame(device!, 1920, TextureFormat.Rgba8Srgb, 1080);
            try
            {
                reference.Legacy = await renderer.PrepareAsync(SceneMutationFixture.Reference(scenario, frame), reference.Target);
                reference.EncodeSubmit(device!);
                await reference.CompleteAsync(null);
                byte[] expected = await reference.ReadAsync(device!);
                SceneMutationFixture.VerifyAnalytic(expected, scenario, frame);
                if (!expected.AsSpan().SequenceEqual(actual))
                {
                    throw new InvalidOperationException("Mutation independent exact pixel comparison failed.");
                }

                checks.Add(new { frame, inputId = SceneMutationFixture.InputId(scenario, frame), exactRgba8 = true, analyticOrder = true, status = "passed", sha256 = Convert.ToHexString(SHA256.HashData(actual)) });
            }
            finally
            {
                if (PublicRetainedApiProbe.QuarantinedObjects.Count == 0)
                {
                    renderer.Dispose();
                }
                else
                {
                    PublicRetainedApiProbe.QuarantinedObjects.Add(renderer);
                }
            }
        }
    }

    private static double Ms(long start) => Stopwatch.GetElapsedTime(start).TotalMilliseconds;
}
