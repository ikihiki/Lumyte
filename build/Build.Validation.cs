using Fallout.Common;

namespace Lumyte.Build;

internal sealed partial class Build
{
    private static readonly string[][] _invocationModes = [[], ["--di"], ["--host"], ["--host", "--di"]];
    private static readonly IReadOnlyDictionary<string, string?> _openAlEnvironment = new Dictionary<string, string?> { ["ALSOFT_DRIVERS"] = "null" };
    private static readonly IReadOnlyDictionary<string, string?> _isolatedPythonEnvironment = new Dictionary<string, string?>
    {
        ["PYTHONHOME"] = null,
        ["PYTHONPATH"] = null,
        ["PYTHONNOUSERSITE"] = "1",
    };

    private async Task TestSignalsAsync()
    {
        await DotNetAsync("test", "tests/Core/Lumyte.Signals.Tests", "-c", Configuration, "--no-build", "--no-restore").ConfigureAwait(false);
        await DotNetAsync("run", "--project", "samples/Lumyte.Signals.Sample", "-c", Configuration, "--no-build", "--no-restore").ConfigureAwait(false);
        await DotNetAsync("test", "tests/Core/Lumyte.Signals.Benchmarks.Tests", "-c", Configuration, "--no-build", "--no-restore").ConfigureAwait(false);
        await DotNetAsync("run", "--project", "samples/Lumyte.UI.Functions.Sample", "-c", Configuration, "--no-build", "--no-restore").ConfigureAwait(false);
    }

    private Target BenchmarkSignals => definition => definition
        .DependsOn(Compile)
        .Executes(async () =>
        {
            await TestSignalsAsync().ConfigureAwait(false);
            await DotNetAsync("run", "--project", "benchmarks/Core/Lumyte.Signals.Benchmark", "-c", Configuration, "--no-build", "--no-restore",
                "--", "--filter", "*", "--artifacts", Path.Combine(ArtifactRoot, "signals-benchmarks")).ConfigureAwait(false);
        });

    private async Task TestTextAsync()
    {
        foreach (string project in new[] { "tests/Graphics/Lumyte.Graphics.Text.Tests", "tests/Graphics/Lumyte.Graphics.Text.Parley.Tests" })
        {
            await DotNetAsync("test", project, "-c", Configuration, "--no-build", "--no-restore").ConfigureAwait(false);
        }
    }

    private async Task PipelineValidationAsync()
    {
        await CaptureValidationLogAsync("vulkan-pipeline-benchmark.jsonl", () => DotNetAsync(
            "run", "--project", "samples/Lumyte.Graphics.DeviceCaps.Sample", "-c", Configuration, "--no-build", "--", "vulkan", "pipeline-benchmark")).ConfigureAwait(false);
    }

    private async Task TestWindowingAsync()
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("The native X11 test target requires Linux and xvfb-run.");
        }

        await ToolAsync("xvfb-run", "-a", DotNetExecutable, "test", "tests/Platform/Lumyte.Platform.Tests", "-c", Configuration,
            "--no-build", "--no-restore", "--filter", "FullyQualifiedName~LinuxNativeTests", "--logger", "trx",
            "--results-directory", Path.Combine(ArtifactRoot, "test-results", "x11")).ConfigureAwait(false);
    }

    private async Task CaptureValidationLogAsync(string name, Func<Task<string>> action)
    {
        string path = Path.Combine(ArtifactRoot, "test-results", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            await File.WriteAllTextAsync(path, await action().ConfigureAwait(false)).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            await File.WriteAllTextAsync(path, exception.ToString()).ConfigureAwait(false);
            throw;
        }
    }
}
