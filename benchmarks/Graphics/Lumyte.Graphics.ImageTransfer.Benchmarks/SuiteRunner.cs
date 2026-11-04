using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Drawing2D;
using Lumyte.Graphics.Incremental.Benchmarks;
using Lumyte.Graphics.Vulkan;

namespace Lumyte.Graphics.Gpu.Benchmarks;

internal static class SuiteRunner
{
    internal static async Task RunAsync(VulkanDevice device, SuiteOptions options)
    {
        var checks = new List<object>();
        var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        string runId = Guid.NewGuid().ToString("N");
        using var identity = new HardwareGpuTimer(device);
        var manifest = new
        {
            schema = "image-transfer-diagnostic-v1",
            runId,
            options,
            device = identity.DeviceDescription,
            caps = device.Caps,
            textureCopyLayout = device.GetTextureCopyLayout(TextureFormat.Rgba8Srgb),
            source = Provenance.Capture(),
            runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            scope = "diagnostic with prior correctness preflight rendering; no formal warmup/process repetitions",
            gpuContract = options.Scenario == "G03" ? "timestamp spans upload barriers+copies+full readback copy+host-read barrier; not CPU mapping" : "draw timestamp spans renderer Record; separate image-upload timestamp spans upload copies/barriers only",
            counters = "GPU bytes are literal copy-region payload bytes issued to public API; mapped bytes include pitch padding; draws from forwarding decorator; no inferred VRAM/eviction counters",
            noDecode = true,
            deterministicInputs = new { version = "image-transfer-v1", seed = 27, seedMeaning = "version marker; no RNG in standard profiles", stateHashes = Enumerable.Range(0, 2).Select(state => options.Scenario == "D06" ? ImageSet.AssetHash(state) : Convert.ToHexString(SHA256.HashData(TransferSlot.Expected(options.Resource == "texture", options.Updates == "dispersed", state)))).ToArray() },
        };
        File.WriteAllText(Path.Combine(options.Output, "manifest.json"), JsonSerializer.Serialize(manifest, json));
        try
        {
            (SuiteSample[] Rows, double Elapsed) result = options.Scenario == "G03" ? await TransfersAsync(device, options, checks) : await ImagesAsync(device, options, checks);
            File.WriteAllLines(Path.Combine(options.Output, "frames.jsonl"), result.Rows.Select(row => JsonSerializer.Serialize(new { runId, options.Scenario, options.Resource, options.Updates, options.Order, options.Depth, row }, json)));
            File.WriteAllLines(Path.Combine(options.Output, "checks.jsonl"), checks.Select(check => JsonSerializer.Serialize(new { runId, check }, json)));
            File.WriteAllText(Path.Combine(options.Output, "summary.json"), JsonSerializer.Serialize(new { runId, status = "passed", performanceUse = "diagnostic only", finalDrainIncluded = true, elapsedMs = result.Elapsed, frames = result.Rows.Length, headlessFramesPerSecond = result.Rows.Length * 1000 / result.Elapsed }, json));
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(options.Output, "summary.json"), JsonSerializer.Serialize(new { runId, status = "failed", comparisonValid = false, error = error.ToString() }, json));
            File.WriteAllLines(Path.Combine(options.Output, "checks.jsonl"), checks.Select(check => JsonSerializer.Serialize(check, json)).Append(JsonSerializer.Serialize(new { status = "failed", error = error.Message }, json)));
            throw;
        }
    }

    private static async Task<(SuiteSample[] Rows, double Elapsed)> TransfersAsync(VulkanDevice device, SuiteOptions options, List<object> checks)
    {
        bool texture = options.Resource == "texture", dispersed = options.Updates == "dispersed";
        TransferSlot[] slots = Enumerable.Range(0, options.Depth).Select(_ => new TransferSlot(device, texture, dispersed)).ToArray();
        byte[][] expected = Enumerable.Range(0, 2).Select(state => TransferSlot.Expected(texture, dispersed, state)).ToArray();
        byte[][][] padded = slots.Select(slot => expected.Select(slot.Pad).ToArray()).ToArray();
        SuiteSample[] rows = Enumerable.Range(1, options.Frames).Select(frame => new SuiteSample { Frame = frame, InputId = InputId(options, frame & 1), GpuUploadBytes = 16384, GpuReadbackBytes = 65536, CopyCalls = 5 }).ToArray();
        var pending = new SuiteSample?[slots.Length];
        try
        {
            foreach (TransferSlot slot in slots)
            {
                byte[] zero = new byte[texture ? checked((int)slot.Pitch * 128) : 65536];
                await slot.SubmitAsync(zero, true);
                await slot.CompleteAsync();
                if ((await slot.ReadAsync()).Any(value => value != 0))
                {
                    throw new InvalidOperationException("Initial transfer destination not zero.");
                }
            }

            for (int state = 0; state < 2; state++)
            {
                await slots[0].SubmitAsync(padded[0][state]);
                await slots[0].CompleteAsync();
                await VerifyAsync(slots[0], state);
            }

            long begin = Stopwatch.GetTimestamp();
            foreach (SuiteSample row in rows)
            {
                int index = (row.Frame - 1) % slots.Length;
                long wait = Stopwatch.GetTimestamp();
                await CompleteAsync(index);
                row.SlotWaitMs = Ms(wait);
                long host = Stopwatch.GetTimestamp();
                await slots[index].SubmitAsync(padded[index][row.Frame & 1]);
                row.HostMs = Ms(host);
                row.MapCopyMs = slots[index].MapCopyMs;
                row.EncodeMs = slots[index].EncodeMs;
                row.SubmitMs = slots[index].SubmitMs;
                row.MappedBytes = padded[index][0].Length;
                row.BytesPerRow = texture ? slots[index].Pitch : 0;
                pending[index] = row;
            }

            for (int i = 0; i < slots.Length; i++)
            {
                await CompleteAsync(i);
            }

            double elapsed = Ms(begin);
            for (int i = 0; i < slots.Length; i++)
            {
                int frame = options.Frames - ((options.Frames - 1 - i + slots.Length) % slots.Length);
                await VerifyAsync(slots[i], frame);
            }

            return (rows, elapsed);
        }
        finally
        {
            foreach (TransferSlot slot in slots)
            {
                slot.Dispose();
            }
        }

        async Task CompleteAsync(int index)
        {
            await slots[index].CompleteAsync();
            if (pending[index] is { } row)
            {
                row.CompletionMs = slots[index].CompletionMs;
                row.GpuMs = slots[index].GpuMs;
                pending[index] = null;
            }
        }

        async Task VerifyAsync(TransferSlot slot, int frame)
        {
            byte[] actual = await slot.ReadAsync();
            byte[] reference = TransferSlot.Expected(texture, dispersed, frame);
            if (!reference.AsSpan().SequenceEqual(actual))
            {
                throw new InvalidOperationException("G03 complete byte-array roundtrip mismatch.");
            }

            checks.Add(new { scenario = "G03", frame, inputId = InputId(options, frame & 1), exactBytes = actual.Length, status = "passed", sha256 = Convert.ToHexString(SHA256.HashData(actual)) });
        }
    }

    private static async Task<(SuiteSample[] Rows, double Elapsed)> ImagesAsync(VulkanDevice device, SuiteOptions options, List<object> checks)
    {
        bool update = options.Updates == "upload", grouped = options.Order == "grouped";
        ImageSet[] images = Enumerable.Range(0, options.Depth).Select(_ => new ImageSet(device, options.Resource == "atlas")).ToArray();
        PublicRetainedApiProbeFrame[] slots = Enumerable.Range(0, options.Depth).Select(_ => new PublicRetainedApiProbeFrame(device, 1920, TextureFormat.Rgba8Srgb, 1080)).ToArray();
        var renderer = new Renderer2D(device, DrawingCommandRenderers2D.CreateDefault());
        DrawList2D[] recordings = images.Select(image => image.Recording(grouped)).ToArray();
        SuiteSample[] rows = Enumerable.Range(1, options.Frames).Select(frame => new SuiteSample { Frame = frame, InputId = InputId(options, update ? frame & 1 : 0), GpuUploadBytes = update ? 4096 : 0, CopyCalls = update ? options.Resource == "atlas" ? 1 : 4 : 0 }).ToArray();
        var pending = new SuiteSample?[slots.Length];
        List<double>[] gpu = slots.Select(_ => new List<double>(1)).ToArray();
        try
        {
            foreach (ImageSet image in images)
            {
                await image.UploadAsync(0);
            }

            var hashes = new List<string>();
            for (int state = 0; state < 2; state++)
            {
                await images[0].UploadAsync(state);
                slots[0].Legacy = await renderer.PrepareAsync(recordings[0], slots[0].Target);
                slots[0].EncodeSubmit(device);
                await slots[0].CompleteAsync(null);
                hashes.Add(await VerifyAsync(slots[0], state, state));
            }

            if (hashes[0] == hashes[1])
            {
                throw new InvalidOperationException("New texture pixels did not visibly change the image.");
            }

            await images[0].UploadAsync(0);
            long begin = Stopwatch.GetTimestamp();
            foreach (SuiteSample row in rows)
            {
                int index = (row.Frame - 1) % slots.Length;
                long wait = Stopwatch.GetTimestamp();
                await CompleteAsync(index);
                row.SlotWaitMs = Ms(wait);
                long host = Stopwatch.GetTimestamp();
                if (update)
                {
                    await images[index].UploadAsync(row.Frame & 1);
                    row.UploadHostMs = images[index].UploadHostMs;
                    row.UploadGpuMs = images[index].UploadGpuMs;
                    row.MappedBytes = images[index].Payloads[0].Sum(payload => payload.Length);
                }

                long prepare = Stopwatch.GetTimestamp();
                slots[index].Legacy = await renderer.PrepareAsync(recordings[index], slots[index].Target);
                row.PrepareMs = Ms(prepare);
                slots[index].EncodeSubmit(device);
                row.HostMs = Ms(host);
                row.EncodeMs = slots[index].EncodeMs;
                row.SubmitMs = slots[index].SubmitMs;
                row.DrawCalls = slots[index].DrawCalls;
                row.BytesPerRow = images[index].Pitch;
                pending[index] = row;
            }

            for (int i = 0; i < slots.Length; i++)
            {
                await CompleteAsync(i);
            }

            double elapsed = Ms(begin);
            for (int i = 0; i < slots.Length; i++)
            {
                int frame = options.Frames - ((options.Frames - 1 - i + slots.Length) % slots.Length);
                await VerifyAsync(slots[i], update ? frame & 1 : 0, frame);
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
                foreach (ImageSet image in images)
                {
                    image.Dispose();
                }
            }
            else
            {
                PublicRetainedApiProbe.QuarantinedObjects.Add(renderer);
                PublicRetainedApiProbe.QuarantinedObjects.AddRange(images);
            }
        }

        async Task CompleteAsync(int index)
        {
            gpu[index].Clear();
            await slots[index].CompleteAsync(gpu[index]);
            if (pending[index] is { } row)
            {
                row.CompletionMs = slots[index].CompletionMs;
                row.GpuMs = gpu[index].Count == 1 ? gpu[index][0] : null;
                pending[index] = null;
            }
        }

        async Task<string> VerifyAsync(PublicRetainedApiProbeFrame target, int state, int frame)
        {
            byte[] actual = await target.ReadAsync(device);
            ImageSet.VerifyAnalytic(actual, state);
            using var reference = new PublicRetainedApiProbeFrame(device, 1920, TextureFormat.Rgba8Srgb, 1080);
            reference.Legacy = await renderer.PrepareAsync(ImageSet.Reference(grouped, state), reference.Target);
            reference.EncodeSubmit(device);
            await reference.CompleteAsync(null);
            byte[] expected = await reference.ReadAsync(device);
            ImageSet.VerifyAnalytic(expected, state);
            if (!expected.AsSpan().SequenceEqual(actual))
            {
                throw new InvalidOperationException("D06 independent analytic Canvas reference differs.");
            }

            string hash = Convert.ToHexString(SHA256.HashData(actual));
            checks.Add(new { scenario = "D06", frame, inputId = InputId(options, state), exactRgba8 = true, analyticSamples = 512, status = "passed", sha256 = hash });
            return hash;
        }
    }

    private static string InputId(SuiteOptions options, int state) => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes($"image-transfer-v1:{options.Scenario}:{(options.Scenario == "D06" ? options.Order : options.Updates)}:{state}")));

    private static double Ms(long start) => Stopwatch.GetElapsedTime(start).TotalMilliseconds;
}
