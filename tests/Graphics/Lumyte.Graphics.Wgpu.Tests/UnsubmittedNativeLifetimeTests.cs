using System.Diagnostics;
using System.Reflection;
using Lumyte.UI.Application.Desktop.Benchmark.BackendCpu;
using Xunit;
using Xunit.Abstractions;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Lumyte.Graphics.Wgpu.Tests;

/// <summary>Runs panic-prone native ownership regressions in bounded, owned subprocesses.</summary>
/// <param name="output">The retained subprocess proof.</param>
public sealed class UnsubmittedNativeLifetimeTests(ITestOutputHelper output)
{
    /// <summary>Checks 2500 warm and cold public recordings and analytic hardware pixels.</summary>
    /// <returns>The bounded native subprocess completion.</returns>
    [Trait("Execution", "Gpu")]
    [HardwareFact]
    public Task PublicRecordingDoesNotExhaustUnsubmittedCommandsAsync() => RunChildAsync("--public-stress");

    /// <summary>Checks native registry counts, reference counts and both submission entry points.</summary>
    /// <returns>The bounded native subprocess completion.</returns>
    [Trait("Execution", "Gpu")]
    [NativeFunctionalFact]
    public Task NativeCommandRegistryRetiresAfterFinalReleaseAsync() => RunChildAsync("--native-fixed");

    private async Task RunChildAsync(string mode)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(Assembly.GetExecutingAssembly().Location);
        start.ArgumentList.Add(mode);
        using Process child = Process.Start(start) ?? throw new InvalidOperationException("Could not start native regression subprocess.");
        Task<string> stdout = child.StandardOutput.ReadToEndAsync();
        Task<string> stderr = child.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try
        {
            await child.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync();
            throw new TimeoutException("Owned native regression subprocess exceeded its deadlock/panic containment deadline.");
        }
        finally
        {
            output.WriteLine(await stdout);
            output.WriteLine(await stderr);
        }

        Assert.Equal(0, child.ExitCode);
        Assert.Contains("PASS", await stdout, StringComparison.Ordinal);
    }

    private sealed class NativeFunctionalFactAttribute : FactAttribute
    {
        public NativeFunctionalFactAttribute()
        {
            if (Environment.GetEnvironmentVariable("LUMYTE_GRAPHICS_GPU_TESTS") != "1"
                && Environment.GetEnvironmentVariable("LUMYTE_UI_BENCHMARK_GPU") != "1")
            {
                Skip = "Set LUMYTE_GRAPHICS_GPU_TESTS=1 for native functional coverage (software adapters allowed), or LUMYTE_UI_BENCHMARK_GPU=1.";
            }
        }
    }

    private sealed class HardwareFactAttribute : FactAttribute
    {
        public HardwareFactAttribute()
        {
            Skip = NativeUiRecordingFixture.UnavailableReason();
        }
    }
}
