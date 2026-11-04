using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Incremental.Benchmarks;
using Lumyte.Graphics.Vulkan;

namespace Lumyte.Graphics.Gpu.Benchmarks;

internal static class OrdinaryRunner
{
    internal static async Task<int> RunAsync(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        string[] allowed = ["scenario", "variant", "depth", "frames", "output"];
        for (int i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || !args[i].StartsWith("--", StringComparison.Ordinal) || !allowed.Contains(args[i][2..]) || !values.TryAdd(args[i][2..], args[i + 1]))
            {
                throw new ArgumentException("Unknown, duplicate or incomplete mutation option.");
            }
        }

        string scenario = values.GetValueOrDefault("scenario", "D04");
        string variant = values.GetValueOrDefault("variant", "pan");
        OrdinaryFixture.Validate(scenario, variant);
        int depth = int.Parse(values.GetValueOrDefault("depth", "1"), System.Globalization.CultureInfo.InvariantCulture);
        int frames = int.Parse(values.GetValueOrDefault("frames", "18"), System.Globalization.CultureInfo.InvariantCulture);
        if (depth is not (1 or 3) || frames is < 18 or > 100000)
        {
            throw new ArgumentException("depth1/3 and frames18..100000 required; D12 must revisit twice.");
        }

        string output = values.GetValueOrDefault("output", "artifacts/graphics-benchmarks/" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(output);
        var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        string runId = Guid.NewGuid().ToString("N");
        VulkanDevice? device = null;
        var checks = new List<object>();
        var visibleHashes = new Dictionary<int, string>();
        try
        {
            device = VulkanDevice.Create();
            using var identity = new HardwareGpuTimer(device);
            var manifest = new
            {
                schema = "ordinary2d-diagnostic-v1",
                runId,
                scenario,
                variant,
                depth,
                frames,
                device = identity.DeviceDescription,
                runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                os = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
                tieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
                tieredPgo = Environment.GetEnvironmentVariable("DOTNET_TieredPGO"),
                icd = Environment.GetEnvironmentVariable("VK_DRIVER_FILES"),
                seed = 27,
                seedMeaning = "reserved deterministic version marker; no pseudorandom inputs in this standard subset",
                provenance = Provenance.Capture(),
                scope = "diagnostic stages + bounded headless throughput; no warmup/pilot/formal campaign yet",
                target = "1920x1080 Rgba8Srgb DPR1; default AA/blend; no presentation",
                cache = new { maxIdleAssets = scenario == "D12" && variant == "small-cache" ? 8 : 512, maxIdleEstimatedBytes = 64 * 1024 * 1024, maxFramesInFlight = 3 },
                trace = "ordinary-v1; see README variant semantics and analytic tolerances",
                counters = "actual successful public Scene edit calls, forwarded API draw calls, public renderer cache stats; no inferred upload/eviction/native VRAM counters",
            };
            File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(manifest, json));

            // Full trace preflight checks initial state and each mutation phase, outside timing.
            await ExecuteAsync(scenario == "D12" ? 18 : 4, 1, true);
            (OrdinarySample[] Rows, double Elapsed) measured = await ExecuteAsync(frames, depth, false);
            File.WriteAllLines(Path.Combine(output, "frames.jsonl"), measured.Rows.Select(row => JsonSerializer.Serialize(new { runId, scenario, variant, depth, row }, json)));
            File.WriteAllLines(Path.Combine(output, "checks.jsonl"), checks.Select(check => JsonSerializer.Serialize(new { runId, check }, json)));
            File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new { runId, status = "passed", performanceUse = "diagnostic only; no formal warmup or independent-process comparison", finalDrainIncluded = true, elapsedMs = measured.Elapsed, headlessFramesPerSecond = frames * 1000 / measured.Elapsed, frames }, json));
            return 0;
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(new { runId, status = "failed", comparisonValid = false, error = error.ToString() }, json));
            File.WriteAllLines(Path.Combine(output, "checks.jsonl"), checks.Select(check => JsonSerializer.Serialize(check, json)).Append(JsonSerializer.Serialize(new { status = "failed", error = error.Message }, json)));
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

        async Task<(OrdinarySample[] Rows, double Elapsed)> ExecuteAsync(int count, int slotCount, bool preflight)
        {
            using var fixture = new OrdinaryFixture(scenario, variant);
            var renderer = new Renderer2D(device!, DrawingCommandRenderers2D.CreateDefault(), retentionOptions: new() { MaxFramesInFlight = 3, MaxCachedAssets = scenario == "D12" && variant == "small-cache" ? 8 : 512, MaxCachedBytes = 64 * 1024 * 1024 });
            PublicRetainedApiProbeFrame[] slots = Enumerable.Range(0, slotCount).Select(_ => new PublicRetainedApiProbeFrame(device!, 1920, TextureFormat.Rgba8Srgb, 1080)).ToArray();
            var pending = new OrdinarySample?[slotCount];
            List<double>[] gpu = Enumerable.Range(0, slotCount).Select(_ => new List<double>(1)).ToArray();
            OrdinarySample[] rows = Enumerable.Range(1, count).Select(frame => new OrdinarySample { Frame = frame, InputId = OrdinaryFixture.InputId(scenario, variant, frame) }).ToArray();
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
                foreach (OrdinarySample row in rows)
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
                    row.VisibleElements = fixture.VisibleElements;
                    row.RerecordedCommands = fixture.RerecordedCommands;
                    row.WorkingSetPage = fixture.WorkingSetPage;
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
            OrdinaryFixture.VerifyAnalytic(actual, scenario, variant, frame);
            var renderer = new Renderer2D(device!, DrawingCommandRenderers2D.CreateDefault());
            using var reference = new PublicRetainedApiProbeFrame(device!, 1920, TextureFormat.Rgba8Srgb, 1080);
            try
            {
                reference.Legacy = await renderer.PrepareAsync(OrdinaryFixture.Reference(scenario, variant, frame), reference.Target);
                reference.EncodeSubmit(device!);
                await reference.CompleteAsync(null);
                byte[] expected = await reference.ReadAsync(device!);
                OrdinaryFixture.VerifyAnalytic(expected, scenario, variant, frame);
                if (!expected.AsSpan().SequenceEqual(actual))
                {
                    throw new InvalidOperationException("Mutation independent exact pixel comparison failed.");
                }

                string hash = Convert.ToHexString(SHA256.HashData(actual));
                if (scenario is "D04" or "D05")
                {
                    visibleHashes[frame & 1] = hash;
                    if (visibleHashes.Count == 2 && visibleHashes[0] == visibleHashes[1])
                    {
                        throw new InvalidOperationException("Requested visible camera/geometry update changed no pixels.");
                    }
                }

                checks.Add(new { frame, inputId = OrdinaryFixture.InputId(scenario, variant, frame), exactRgba8 = true, analyticOrder = true, status = "passed", sha256 = hash });
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
