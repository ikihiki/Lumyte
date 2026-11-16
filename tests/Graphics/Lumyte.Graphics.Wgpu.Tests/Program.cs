using System.Runtime.InteropServices;
using Ahjo.Wgpu.Native;
using Lumyte.Graphics.Abstractions;
using Lumyte.Graphics.Wgpu;
using Lumyte.Graphics.Wgpu.Tests;
using Lumyte.UI.Application.Desktop.Benchmark.BackendCpu;

bool nativeFunctionalOptIn = args.Contains("--native-fixed", StringComparer.Ordinal)
    && Environment.GetEnvironmentVariable("LUMYTE_GRAPHICS_GPU_TESTS") == "1";
if (!nativeFunctionalOptIn && NativeUiRecordingFixture.UnavailableReason() is { } reason)
{
    string prerequisite = args.Contains("--native-fixed", StringComparer.Ordinal)
        ? "Set LUMYTE_GRAPHICS_GPU_TESTS=1 for native functional coverage, or LUMYTE_UI_BENCHMARK_GPU=1."
        : reason;
    Console.WriteLine($"SKIP: {prerequisite}");
    return;
}

if (args.Contains("--native-upstream", StringComparer.Ordinal))
{
    string upstream = Environment.GetEnvironmentVariable("LUMYTE_WGPU_UPSTREAM_NATIVE")
        ?? throw new InvalidOperationException("Supply the original native binary path for the negative control.");
    NativeLibrary.SetDllImportResolver(typeof(WGPU).Assembly, (name, assembly, searchPath) =>
        name == "wgpu_native" ? NativeLibrary.Load(upstream) : 0);
    NativeOwnershipProbe.Run();
    return;
}

if (args.Contains("--native-fixed", StringComparer.Ordinal))
{
    using var device = WgpuDevice.Create();
    NativeOwnershipProbe.Run();
    return;
}

WgpuDevice.InitializeNativeRuntime();
using var fixture = new NativeUiRecordingFixture();
await fixture.StartAsync();
Console.WriteLine(fixture.AdapterIdentity);
int submissions = fixture.ValidationSubmissions;
long reused = fixture.WarmStatistics.ReusedRuns;
for (int iteration = 0; iteration < 2500; iteration++)
{
    if (await fixture.WarmRecordAsync() != CommandBufferState.Disposed ||
        await fixture.ColdRendererRecordAsync() != CommandBufferState.Disposed ||
        fixture.WarmStatistics.ActiveFrames != 0 || fixture.ValidationSubmissions != submissions)
    {
        throw new InvalidOperationException($"Unsubmitted ownership/submission contract failed at {iteration}.");
    }

    if ((iteration + 1) % 100 == 0)
    {
        Console.WriteLine($"discarded warm={iteration + 1} cold={iteration + 1}; submissions={submissions}; active=0");
    }
}

if (fixture.WarmStatistics.ReusedRuns <= reused)
{
    throw new InvalidOperationException("Warm retained resources were not reused.");
}

await fixture.ValidatePixelsAsync();
if (fixture.ValidationSubmissions != submissions + 2 || fixture.WarmStatistics.ActiveFrames != 0)
{
    throw new InvalidOperationException("Final pixel-validation ownership contract failed.");
}

Console.WriteLine("PASS: 2500 warm + 2500 cold unsubmitted recordings; no recording submits/waits; warm/cold full analytic pixels before and after; active frames=0.");
