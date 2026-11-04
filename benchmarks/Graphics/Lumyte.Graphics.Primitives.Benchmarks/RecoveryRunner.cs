using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Ahjo.Wgpu.Native;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Hosting;
using Lumyte.Graphics.Wgpu;
using Microsoft.Extensions.DependencyInjection;
using A = Ahjo.Wgpu;

namespace Lumyte.Graphics.Primitives.Benchmarks;

/// <summary>Runs an isolated native WebGPU destroy, Hosting retirement and resource reconstruction trace.</summary>
internal static class RecoveryRunner
{
    private static int _started;

    internal static async Task<int> RunAsync(string[] args)
    {
        (string output, int cycles) = Parse(args);
        Program.Require(Interlocked.Exchange(ref _started, 1) == 0, "Recovery requires an independent fresh process.");
        Program.Require(!Directory.Exists(output), "Recovery output directory already exists; choose a fresh run ID.");
        Directory.CreateDirectory(Path.Combine(output, "images"));
        var rows = new List<object>();
        var checks = new List<object>();
        var devices = new List<object>();
        var services = new ServiceCollection();
        services.AddGraphicsDevice<RecoveryFactory>(ServiceLifetime.Singleton);
        ServiceProvider provider = services.BuildServiceProvider();
        IGraphicsDeviceService service = provider.GetRequiredService<IGraphicsDeviceService>();
        RecoveryFactory factory = provider.GetRequiredService<RecoveryFactory>();
        GraphicsDeviceLease? lease = null;
        RecoveryResources? resources = null;
        string status = "failed";
        string? error = null;
        object? provenance = null;
        long runStart = Stopwatch.GetTimestamp();
        void Stage(string name, int cycle, long start) => rows.Add(new
        {
            caseId = "G06",
            mode = "recovery",
            profile = "native-webgpu-process-local",
            cycle,
            phase = name,
            epoch = service.State.Epoch,
            hostElapsedMs = Program.Ms(start),
            gpuSpanMs = (double?)null,
        });
        try
        {
            provenance = Lumyte.Graphics.Gpu.Benchmarks.Provenance.Capture();
            Memory("before-device", checks);
            long start = Stopwatch.GetTimestamp();
            long epochStart = start;
            await service.InitializeAsync();
            Stage("device-initialize", 0, start);
            lease = await service.AcquireAsync();
            devices.Add(Identity((WgpuDevice)lease.Device, lease.State.Epoch));
            start = Stopwatch.GetTimestamp();
            resources = RecoveryResources.Create(lease.Device);
            Stage("resource-create", 0, start);
            await RoundtripAsync(resources, lease.State.Epoch, 0, epochStart, output, rows, checks);
            Memory("initial-resident", checks);
            for (int cycle = 1; cycle <= cycles; cycle++)
            {
                GraphicsDeviceLease oldLease = lease;
                GraphicsDeviceState oldState = lease.State;
                var oldDevice = (WgpuDevice)lease.Device;
                int callbacks = 0;
                oldDevice.DeviceLost += (_, _) => callbacks++;
                start = Stopwatch.GetTimestamp();
                Destroy(oldDevice);
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                while (oldDevice.Status == GraphicsDeviceStatus.Ready)
                {
                    timeout.Token.ThrowIfCancellationRequested();
                    oldDevice.ProcessEvents();
                    await Task.Delay(1, timeout.Token);
                }

                Stage("native-destroy-to-loss-callback", cycle, start);
                Program.Require(callbacks == 1 && oldDevice.Status == GraphicsDeviceStatus.Lost && service.State.Status == GraphicsDeviceServiceStatus.Lost, "Native callback did not invalidate Hosting.");
                Program.Require(!lease.IsUsable && service.State.InvalidatedThroughVersion >= oldState.Version, "Old epoch lease/watermark remained valid.");
                bool rejected = false;
                try
                {
                    _ = lease.Device;
                }
                catch (GraphicsDeviceLostException)
                {
                    rejected = true;
                }

                Program.Require(rejected, "Old lease returned the destroyed native device.");
                rejected = false;
                try
                {
                    using GraphicsDeviceLease invalid = await service.AcquireAsync();
                }
                catch (GraphicsDeviceUnavailableException)
                {
                    rejected = true;
                }

                Program.Require(rejected, "Acquire accepted a lost epoch.");
                start = Stopwatch.GetTimestamp();
                await service.WaitForRetirementAsync(oldState.Epoch, timeout.Token);
                Stage("native-retirement-confirmation", cycle, start);
                start = Stopwatch.GetTimestamp();
                resources.ConfirmRetired();
                resources.Dispose();
                Program.Require(resources.Disposed && resources.ReleasedChildren > 0, "Old child cleanup did not complete.");
                int releasedChildren = resources.ReleasedChildren;
                resources = null;
                Program.Require(await service.RetireAsync(timeout.Token) == 0, "Hosting released an owner while its lease remained retained.");
                lease.Dispose();
                Program.Require(!lease.IsUsable, "Returned old lease remained usable.");
                lease = null;
                Program.Require(await service.RetireAsync(timeout.Token) == 1 && oldDevice.Status == GraphicsDeviceStatus.Disposed, "Old native owner did not retire.");
                Stage("old-children-lease-owner-release", cycle, start);
                checks.Add(new
                {
                    check = "native-loss-and-old-epoch-release",
                    cycle,
                    oldState.Epoch,
                    callbacks,
                    releasedChildren,
                    status = "passed",
                    oldDeviceDisposed = true,
                });
                Memory("old-epoch-released-" + cycle, checks);
                start = Stopwatch.GetTimestamp();
                epochStart = start;
                GraphicsDeviceState recovered = await service.RecoverAsync(timeout.Token);
                Stage("hosting-recover-device", cycle, start);
                Program.Require(recovered.Status == GraphicsDeviceServiceStatus.Ready && recovered.Epoch > oldState.Epoch && recovered.Version > recovered.InvalidatedThroughVersion, "Recovery reused an invalid epoch/version.");
                Program.Require(!oldLease.IsUsable, "Recovered service revalidated a returned old-epoch lease.");
                checks.Add(new
                {
                    check = "recovered-epoch-state",
                    status = "passed",
                    cycle,
                    oldEpoch = oldState.Epoch,
                    oldReadyVersion = oldState.Version,
                    newEpoch = recovered.Epoch,
                    readyVersion = recovered.Version,
                    invalidatedThroughVersion = recovered.InvalidatedThroughVersion,
                    oldLeaseUsable = oldLease.IsUsable,
                });
                lease = await service.AcquireAsync();
                Program.Require(!ReferenceEquals(oldDevice, lease.Device), "Recovery reused the destroyed physical instance.");
                devices.Add(Identity((WgpuDevice)lease.Device, lease.State.Epoch));
                start = Stopwatch.GetTimestamp();
                resources = RecoveryResources.Create(lease.Device);
                Stage("resource-rebuild", cycle, start);
                await RoundtripAsync(resources, recovered.Epoch, cycle, epochStart, output, rows, checks);
                Memory("recovered-resident-" + cycle, checks);
            }

            long cleanup = Stopwatch.GetTimestamp();
            resources.Dispose();
            Program.Require(resources.Disposed, "Final children not disposed.");
            checks.Add(new
            {
                check = "final-child-release",
                status = "passed",
                resources.ReleasedChildren,
            });
            resources = null;
            lease.Dispose();
            lease = null;
            await provider.DisposeAsync();
            Program.Require(factory.Owners.Count == cycles + 1 && factory.Owners.All(owner => owner.Disposed), "Not all native owners were disposed.");
            Stage("final-children-lease-scope-release", cycles, cleanup);
            Memory("after-final-release", checks);
            Program.Require(Program.Quarantine.Count == 0, "Successful trace retained quarantined owners.");
            status = "passed";
        }
        catch (Exception failure)
        {
            error = failure.ToString();
            checks.Add(new
            {
                check = "run",
                status = "failed",
                error,
            });

            // A timeout, uncertain Submit, map or cleanup failure is never authority to free native owners.
            Program.Quarantine.Add(provider);
            Program.Quarantine.Add(factory);
            if (lease is not null)
            {
                Program.Quarantine.Add(lease);
            }

            if (resources is not null)
            {
                Program.Quarantine.Add(resources);
            }
        }

        var manifest = new
        {
            schema = "graphics-recovery/1",
            scenarioVersion = "adr27-g06-native-webgpu-v1",
            status,
            error,
            runId = Path.GetFileName(output),
            processId = Environment.ProcessId,
            cycles,
            provenance,
            actualDevices = devices,
            backend = "WebGPU/wgpu-native",
            fault = "wgpuDeviceDestroy on this runner's independently owned instance; native callback observed; no host/driver reset",
            seed = 0,
            inputSha256 = Program.Hash(JsonSerializer.SerializeToUtf8Bytes(Spec())),
            fontAssets = Array.Empty<string>(),
            fontReason = "No text or font input in the analytic model fixture.",
            shaderHashScope = "concatenated model vertex and fragment WGSL target bytes before runtime binding specialization; binary/container/source hashes also in provenance",
            target = new
            {
                width = Program.Size,
                height = Program.Size,
                format = "Rgba8Unorm",
                dpr = 1,
                aa = "product-default",
                filtering = "product-default",
            },
            runtime = RuntimeInformation.FrameworkDescription,
            environment = new[] { "VK_DRIVER_FILES", "VK_ICD_FILENAMES", "WGPU_BACKEND", "DOTNET_TieredCompilation", "DOTNET_TieredPGO", "DOTNET_ReadyToRun", "DOTNET_gcServer" }.ToDictionary(name => name, Environment.GetEnvironmentVariable),
            elapsedIncludingValidationAndMemoryProbesMs = Program.Ms(runStart),
            memory = "Full-GC managed live heap and RSS sampled only at named lifecycle checkpoints; sampled RSS high-water is not guaranteed transient peak. OS PeakWorkingSetSize separately recorded. Native allocation and VRAM unknown/null.",
            gpuSpanMs = (double?)null,
            nativeAllocationBytes = (long?)null,
            nativeVramBytes = (long?)null,
            boundaries = "Lifecycle host-wall intervals separate from ordinary warm frames; roundtrip readback, pixel comparison, JSON, provenance and full-GC probes excluded from timed encode/submit/completion stages; fresh process does not imply cold driver disk cache.",
            limitations = new[] { "32x32 analytic headless single-flight diagnostic, not full-HD or presentation", "drained work before native destruction; not pending-work loss coverage", "raw ModelMesh replacement and rebuild; no ResourceManager/file decode or in-place mutation", "not a formal five-process A/B or warm stability campaign", "no timestamp instrumentation, native allocation or VRAM measurement" },
            quarantinedOwners = Program.Quarantine.Count,
        };
        File.WriteAllText(Path.Combine(output, "manifest.json"), JsonSerializer.Serialize(manifest, Program.Json));
        File.WriteAllLines(Path.Combine(output, "frames.jsonl"), rows.Select(row => JsonSerializer.Serialize(row, Program.Json)));
        File.WriteAllLines(Path.Combine(output, "checks.jsonl"), checks.Select(check => JsonSerializer.Serialize(check, Program.Json)));
        var summary = new
        {
            status,
            cycles,
            lifecycleSamplesValid = status == "passed",
            formalComparisonValid = false,
            measuredWarmFrames = 0,
        };
        File.WriteAllText(Path.Combine(output, "summary.json"), JsonSerializer.Serialize(summary, Program.Json));
        Console.WriteLine(output + ": " + status);
        return status == "passed" ? 0 : 1;
    }

    internal static (string Output, int Cycles) Parse(string[] args)
    {
        string output = "artifacts/graphics-benchmarks/recovery-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff", CultureInfo.InvariantCulture);
        int cycles = 1;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        Program.Require(args.Contains("--recovery", StringComparer.Ordinal), "Explicit --recovery is required.");

        for (int index = 0; index < args.Length; index++)
        {
            string option = args[index];
            if (!seen.Add(option))
            {
                throw new ArgumentException("Duplicate option: " + option);
            }

            if (option == "--recovery")
            {
                continue;
            }

            if (option is not ("--output" or "--cycles"))
            {
                throw new ArgumentException("Recovery cannot mix ordinary/unknown flags: " + option);
            }

            if (++index >= args.Length || string.IsNullOrWhiteSpace(args[index]) || args[index].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException("Missing value for " + option);
            }

            if (option == "--output")
            {
                output = args[index];
            }
            else if (!int.TryParse(args[index], NumberStyles.None, CultureInfo.InvariantCulture, out cycles) || cycles is < 1 or > 100)
            {
                throw new ArgumentException("Cycles must be 1..100.");
            }
        }

        return (output, cycles);
    }

    internal static void CheckPixels(byte[] pixels, bool green)
    {
        Program.Require(pixels.Length == Program.Size * Program.Size * 4, "Unexpected readback size.");
        for (int offset = 0; offset < pixels.Length; offset += 4)
        {
            Program.Require(Math.Abs(pixels[offset] - (green ? 0 : 255)) <= 1 && Math.Abs(pixels[offset + 1] - (green ? 255 : 0)) <= 1 && pixels[offset + 2] == 0 && Math.Abs(pixels[offset + 3] - 255) <= 1, "Analytic red/green reference mismatch at byte " + offset);
        }
    }

    private static Program.Case Spec() => new("G06", "native-webgpu-recovery", 1, Model: true, Replace: true);

    private static async Task RoundtripAsync(RecoveryResources resources, ulong epoch, int cycle, long epochStart, string output, List<object> rows, List<object> checks)
    {
        for (int frame = 0; frame < 3; frame++)
        {
            long first = Stopwatch.GetTimestamp();
            await resources.PrepareAsync(frame);
            double prepare = Program.Ms(first);
            long start = Stopwatch.GetTimestamp();
            resources.Encode();
            double encode = Program.Ms(start);
            start = Stopwatch.GetTimestamp();
            resources.Submit();
            double submit = Program.Ms(start);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await resources.CompleteAsync(timeout.Token);
            double completion = Program.Ms(start);
            double firstOutput = Program.Ms(first);
            double? epochFirstOutput = frame == 0 ? Program.Ms(epochStart) : null;
            resources.ReleaseFrame();
            byte[] pixels = await resources.ReadPixelsAsync(timeout.Token);
            CheckPixels(pixels, frame == 1);
            resources.ReleaseFrame();
            string image = "images/epoch-" + epoch + "-frame-" + frame + ".rgba";
            File.WriteAllBytes(Path.Combine(output, image), pixels);
            checks.Add(new
            {
                check = "analytic-roundtrip",
                status = "passed",
                cycle,
                epoch,
                frame,
                inputId = frame == 1 ? "green" : "red",
                sha256 = Program.Hash(pixels),
                image,
                resources.ShaderHash,
            });
            rows.Add(new
            {
                caseId = "G06",
                mode = "recovery",
                cycle,
                epoch,
                frame,
                phase = frame == 0 ? "first-output" : "asset-replacement",
                prepareMs = prepare,
                encodeMs = encode,
                submitMs = submit,
                completionLatencyMs = completion,
                firstOutputCompletedMs = firstOutput,
                fromDeviceRequestToFirstOutputMs = epochFirstOutput,
                gpuSpanMs = (double?)null,
                resources.DrawCalls,
                resources.CommandCalls,
                resources.SubmitCalls,
                resources.ShaderHash,
            });
        }
    }

    private static void Memory(string checkpoint, List<object> checks)
    {
        long live = GC.GetTotalMemory(true);
        using var process = Process.GetCurrentProcess();
        process.Refresh();
        checks.Add(new
        {
            check = "memory-checkpoint",
            checkpoint,
            managedLiveAfterFullGcBytes = live,
            processRssBytes = process.WorkingSet64,
            osPeakWorkingSetBytes = process.PeakWorkingSet64,
            nativeAllocationBytes = (long?)null,
            nativeVramBytes = (long?)null,
        });
    }

    private static unsafe object Identity(WgpuDevice device, ulong epoch)
    {
        A.Adapter adapter = GetAdapter(device);
        WGPUAdapterInfo info = default;
        Program.Require(WGPU.wgpuAdapterGetInfo(adapter.Handle, &info) == WGPUStatus.Success, "Native adapter identity query failed.");
        try
        {
            Program.Require(info.adapterType != WGPUAdapterType.CPU && info.adapterType != WGPUAdapterType.Unknown, "Recovery hardware run requires an identified physical GPU, not a CPU/unknown adapter.");
            return new
            {
                epoch,
                vendor = NativeString(info.vendor),
                architecture = NativeString(info.architecture),
                device = NativeString(info.device),
                driverDescription = NativeString(info.description),
                backendType = info.backendType.ToString(),
                adapterType = info.adapterType.ToString(),
                vendorId = info.vendorID,
                deviceId = info.deviceID,
                shaderTarget = device.Caps.ShaderTarget.ToString(),
            };
        }
        finally
        {
            WGPU.wgpuAdapterInfoFreeMembers(info);
        }
    }

    private static unsafe string NativeString(WGPUStringView value) => value.data == null ? string.Empty : value.length == nuint.MaxValue ? Marshal.PtrToStringUTF8((nint)value.data) ?? string.Empty : Encoding.UTF8.GetString(new ReadOnlySpan<byte>(value.data, checked((int)value.length)));

    private static unsafe void Destroy(WgpuDevice device)
    {
        // Same supported instance-local native path as Hosting.Tests.NativeDeviceLossTests.
        A.Device native = GetNativeDevice(device);
        WGPU.wgpuDeviceDestroy(native.Handle);
        var desc = new WGPUCommandEncoderDescriptor { label = new() { length = nuint.MaxValue } };
        WGPUCommandEncoderImpl* encoder = WGPU.wgpuDeviceCreateCommandEncoder(native.Handle, &desc);
        if (encoder != null)
        {
            WGPU.wgpuCommandEncoderRelease(encoder);
        }
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_NativeDevice")]
    private static extern A.Device GetNativeDevice(WgpuDevice device);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_adapter")]
    private static extern ref A.Adapter GetAdapter(WgpuDevice device);

    /// <summary>Creates only this process's independently owned native WebGPU instances.</summary>
    internal sealed class RecoveryFactory : IGraphicsDeviceFactory
    {
        /// <summary>Initializes a new instance of the <see cref="RecoveryFactory"/> class.</summary>
        public RecoveryFactory()
        {
        }

        internal List<RecoveryOwner> Owners { get; } = new();

        /// <inheritdoc />
        public ValueTask<IGraphicsDeviceOwner> CreateAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var owner = new RecoveryOwner(WgpuDevice.Create());
            Owners.Add(owner);
            return ValueTask.FromResult<IGraphicsDeviceOwner>(owner);
        }
    }

    /// <summary>Transfers native lifetime, without forwarding device operations, to Hosting.</summary>
    internal sealed class RecoveryOwner(WgpuDevice device) : IGraphicsDeviceOwner
    {
        /// <inheritdoc />
        public IGraphicDevice Device => device;

        internal bool Disposed { get; private set; }

        /// <inheritdoc />
        public ValueTask DisposeAsync()
        {
            device.Dispose();
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }
}
