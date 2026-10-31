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

    [Parameter("Repository whose successful diagnostic gallery artifacts are collected.")]
    public string? GalleryRepository { get; set; }

    private Target SetupWasm => definition => definition
        .Executes(InstallWasmToolsAsync);

    private Target TestWasm => definition => definition
        .DependsOn(SetupWasm)
        .Executes(async () =>
        {
            string graphics = Path.Combine(ArtifactRoot, "browser", "graphics");
            string text = Path.Combine(ArtifactRoot, "browser", "text");
            await DotNetAsync("publish", "samples/Lumyte.Graphics.Browser.Sample", "-c", Configuration, "-o", graphics).ConfigureAwait(false);
            await CaptureValidationLogAsync("browser-webgpu.log", () => NodeAsync("tools/graphics/verify-browser.mjs", Path.Combine(graphics, "wwwroot"))).ConfigureAwait(false);
            await DotNetAsync("publish", "samples/Lumyte.Graphics.Text.Parley.Browser.Sample", "-c", Configuration, "-o", text).ConfigureAwait(false);
            await CaptureValidationLogAsync("browser-text-webgpu.log", () => NodeAsync("samples/Lumyte.Graphics.Text.Parley.Browser.Sample/verify-browser.mjs", Path.Combine(text, "wwwroot"))).ConfigureAwait(false);
        });

    private Target TestBrowserContracts => definition => definition
        .Executes(async () =>
        {
            await NodeAsync("--test", "tests/Graphics/Lumyte.Graphics.Hosting.Tests/browser-device-loss.test.mjs").ConfigureAwait(false);
            await NodeAsync("--test", "tests/Audio/browser-audio.test.mjs").ConfigureAwait(false);
        });

    private Target TestSignals => definition => definition
        .DependsOn(Compile)
        .Executes(async () =>
        {
            await DotNetAsync("test", "tests/Core/Lumyte.Signals.Tests", "-c", Configuration, "--no-build", "--no-restore").ConfigureAwait(false);
            await DotNetAsync("run", "--project", "samples/Lumyte.Signals.Sample", "-c", Configuration, "--no-build", "--no-restore").ConfigureAwait(false);
        });

    private Target TestText => definition => definition
        .DependsOn(Compile)
        .Executes(async () =>
        {
            foreach (string project in new[] { "tests/Graphics/Lumyte.Graphics.Text.Tests", "tests/Graphics/Lumyte.Graphics.Text.Parley.Tests" })
            {
                await DotNetAsync("test", project, "-c", Configuration, "--no-build", "--no-restore").ConfigureAwait(false);
            }
        });

    private Target PipelineBenchmark => definition => definition
        .DependsOn(Compile)
        .Executes(async () =>
        {
            await CaptureValidationLogAsync("vulkan-pipeline-benchmark.jsonl", () => DotNetAsync(
                "run", "--project", "samples/Lumyte.Graphics.DeviceCaps.Sample", "-c", Configuration, "--no-build", "--", "vulkan", "pipeline-benchmark")).ConfigureAwait(false);
        });

    private Target TestWindowing => definition => definition
        .DependsOn(Compile)
        .Executes(async () =>
        {
            if (!OperatingSystem.IsLinux())
            {
                throw new PlatformNotSupportedException("The native X11 test target requires Linux and xvfb-run.");
            }

            await ToolAsync("xvfb-run", "-a", DotNetExecutable, "test", "tests/Platform/Lumyte.Platform.Tests", "-c", Configuration,
                "--no-build", "--no-restore", "--filter", "FullyQualifiedName~LinuxNativeTests", "--logger", "trx",
                "--results-directory", Path.Combine(ArtifactRoot, "test-results", "x11")).ConfigureAwait(false);
            await NodeAsync("--test", "tests/Platform/browser-windowing.test.mjs").ConfigureAwait(false);
        });

    private Target TestBrowserAudio => definition => definition
        .DependsOn(SetupWasm)
        .Executes(async () =>
        {
            string environment = Path.Combine(ArtifactRoot, "build-tools", "audio-playwright");
            string python = OperatingSystem.IsWindows() ? Path.Combine(environment, "Scripts", "python.exe") : Path.Combine(environment, "bin", "python");
            if (!File.Exists(python))
            {
                await PythonAsync("-m", "venv", environment).ConfigureAwait(false);
            }

            await ToolWithEnvironmentAsync(python, _isolatedPythonEnvironment, "-m", "pip", "install", "--disable-pip-version-check", "playwright==1.58.0").ConfigureAwait(false);
            foreach ((string project, string directory, string log) in new[]
            {
                ("samples/Lumyte.Audio.Execution.Browser.Sample", "audio-execution-browser", "browser-audio-execution.log"),
                ("samples/Lumyte.Audio.Browser.Sample", "audio-browser", "browser-audio.log"),
            })
            {
                string output = Path.Combine(ArtifactRoot, directory);
                await DotNetAsync("publish", project, "-c", Configuration, "-o", output).ConfigureAwait(false);
                var arguments = new List<string> { "tools/audio/verify_browser.py", Path.Combine(output, "wwwroot") };
                if (Environment.GetEnvironmentVariable("CHROME") is string chrome && !string.IsNullOrWhiteSpace(chrome))
                {
                    arguments.AddRange(["--chromium", chrome]);
                }

                await CaptureValidationLogAsync(log, () => ToolWithEnvironmentAsync(python, _isolatedPythonEnvironment, arguments.ToArray())).ConfigureAwait(false);
            }
        });

    private Target TestDiagnosticsProcesses => definition => definition
        .DependsOn(Compile)
        .Executes(async () =>
        {
            await PythonAsync("tools/diagnostics/communication-smoke.py", "--dotnet", DotNetExecutable,
                "--configuration", Configuration, "--output", Path.Combine(ArtifactRoot, "test-results", "diagnostics-communication", "processes")).ConfigureAwait(false);
        });

    private Target TestDiagnosticsUi => definition => definition
        .DependsOn(Compile)
        .Executes(async () =>
        {
            await DotNetAsync("publish", "src/Diagnostics/Lumyte.Diagnostics.Server", "-c", Configuration, "--no-build", "--no-restore",
                "-o", Path.Combine(ArtifactRoot, "diagnostics-ui-server")).ConfigureAwait(false);
            await ToolWithEnvironmentAsync(NodeExecutable, new Dictionary<string, string?>
            {
                ["DOTNET"] = DotNetExecutable,
                ["LUMYTE_CONFIGURATION"] = Configuration,
            }, "tools/diagnostics/verify-browser.mjs").ConfigureAwait(false);
        });

    private Target DiagnosticGallery => definition => definition
        .Executes(async () =>
        {
            await NodeAsync("tools/diagnostics/generate-gallery.test.mjs").ConfigureAwait(false);
            await NodeAsync("tools/diagnostics/assemble-gallery-site.test.mjs").ConfigureAwait(false);
            await PythonAsync("-m", "unittest", "discover", "-s", "tools/diagnostics", "-p", "test_collect_gallery_artifacts.py").ConfigureAwait(false);
            await NodeAsync("tools/diagnostics/generate-gallery.mjs", Path.Combine(ArtifactRoot, "test-results", "diagnostics-ui"),
                Path.Combine(ArtifactRoot, "diagnostics-gallery")).ConfigureAwait(false);
        });

    private Target DiagnosticPreviews => definition => definition
        .Executes(async () =>
        {
            string repository = GalleryRepository ?? Environment.GetEnvironmentVariable("GITHUB_REPOSITORY")
                ?? throw new ArgumentException("DiagnosticPreviews requires --gallery-repository or GITHUB_REPOSITORY.");
            string input = Path.Combine(ArtifactRoot, "diagnostics-incoming");
            await PythonAsync("tools/diagnostics/collect-gallery-artifacts.py", "--repository", repository, "--output", input).ConfigureAwait(false);
            await NodeAsync("tools/diagnostics/assemble-gallery-site.mjs", Path.Combine(input, "catalog.json"),
                Path.Combine(ArtifactRoot, "diagnostics-site")).ConfigureAwait(false);
        });

    private Target Samples => definition => definition
        .DependsOn(Compile)
        .Executes(RunSamplesAsync);

    private Target TestAot => definition => definition
        .DependsOn(Compile)
        .Executes(RunAotSamplesAsync);

    private Target PublishWindowSample => definition => definition
        .DependsOn(Compile)
        .Executes(async () =>
        {
            await DotNetAsync("publish", "samples/Lumyte.Platform.Windows.Sample", "-c", Configuration, "--no-build", "--no-restore",
                "-o", Path.Combine(ArtifactRoot, "single-window-sample")).ConfigureAwait(false);
        });

    private Target CheckMarkdown => definition => definition
        .Executes(async () =>
        {
            await NodeAsync(await FindMarkdownLintAsync().ConfigureAwait(false)).ConfigureAwait(false);
        });

    private Target FormatMarkdown => definition => definition
        .Executes(async () =>
        {
            await NodeAsync(await FindMarkdownLintAsync().ConfigureAwait(false), "--fix").ConfigureAwait(false);
        });

    private Target CheckNativeFormat => definition => definition
        .Executes(async () =>
        {
            await FormatNativeSourcesAsync(fix: false).ConfigureAwait(false);
        });

    private Target FormatNative => definition => definition
        .Executes(async () =>
        {
            await FormatNativeSourcesAsync(fix: true).ConfigureAwait(false);
        });

    private async Task RunSamplesAsync()
    {
        const string Audio = "tests/Audio/Lumyte.Resources.Audio.IntegrationTests";
        foreach (string? scenario in new[] { "--reload", null, "--recover", "--recover-stop" })
        {
            foreach (string[] mode in _invocationModes)
            {
                await RunManagedSampleAsync(Audio, scenario is null ? mode : [scenario, .. mode]).ConfigureAwait(false);
            }
        }

        await RunManagedSampleAsync(Audio, "--recover-start-cancel", "--host").ConfigureAwait(false);
        await RunManagedSampleAsync(Audio, "--recover-start-cancel", "--host", "--di").ConfigureAwait(false);
        foreach (string[] mode in _invocationModes)
        {
            await RunManagedSampleAsync("samples/Lumyte.Execution.Sample", mode).ConfigureAwait(false);
        }

        foreach (string project in new[]
        {
            "samples/Lumyte.Signals.Sample",
            "samples/Lumyte.Composition.Sample", "samples/Lumyte.Resources.Sample", "samples/Lumyte.Diagnostics.Sample",
            "samples/Lumyte.Animation.Sample", "samples/Lumyte.StateMachines.Sample", "samples/Lumyte.Animation.StateMachine.Sample",
        })
        {
            await RunManagedSampleAsync(project).ConfigureAwait(false);
        }

        Directory.CreateDirectory(ArtifactRoot);
        string settings = Path.Combine(ArtifactRoot, "sample-input-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await RunManagedSampleAsync("samples/Lumyte.Input.Advanced.Sample", settings).ConfigureAwait(false);
        }
        finally
        {
            File.Delete(settings);
        }

        await RunOpenAlSampleAsync("samples/Lumyte.Audio.Sample").ConfigureAwait(false);
        string[][] openAlInvocations = [["--openal"], ["--openal", "--host"], ["--openal", "--reload"], ["--openal", "--host", "--reload"]];
        foreach (string[] arguments in openAlInvocations)
        {
            await RunOpenAlSampleAsync(Audio, arguments).ConfigureAwait(false);
        }

        foreach (string[] mode in _invocationModes)
        {
            await RunOpenAlSampleAsync("samples/Lumyte.Audio.Execution.Sample", mode).ConfigureAwait(false);
        }
    }

    private async Task RunAotSamplesAsync()
    {
        string execution = await PublishAotSampleAsync("samples/Lumyte.Execution.Sample", "execution-aot", "Lumyte.Execution.Sample", strict: false).ConfigureAwait(false);
        foreach (string[] mode in _invocationModes)
        {
            await ToolAsync(execution, mode).ConfigureAwait(false);
        }

        string signals = await PublishAotSampleAsync("samples/Lumyte.Signals.Sample", "signals-aot", "Lumyte.Signals.Sample", strict: true).ConfigureAwait(false);
        await ToolAsync(signals).ConfigureAwait(false);
        string resources = await PublishAotSampleAsync("tests/Core/Lumyte.Resources.AotSmoke", "resources-aot", "Lumyte.Resources.AotSmoke", strict: false).ConfigureAwait(false);
        await ToolAsync(resources).ConfigureAwait(false);
        await ToolAsync(resources, "--host").ConfigureAwait(false);
        string graphics = await PublishAotSampleAsync("tests/Graphics/Lumyte.Resources.Graphics.AotSmoke", "resource-graphics-aot", "Lumyte.Resources.Graphics.AotSmoke", strict: true).ConfigureAwait(false);
        await ToolAsync(graphics).ConfigureAwait(false);
        await ToolAsync(graphics, "--host").ConfigureAwait(false);
        string audio = await PublishAotSampleAsync("tests/Audio/Lumyte.Resources.Audio.IntegrationTests", "resources-audio-aot", "Lumyte.Resources.Audio.IntegrationTests", strict: true).ConfigureAwait(false);
        foreach (string[] mode in _invocationModes)
        {
            await ToolAsync(audio, mode).ConfigureAwait(false);
            await ToolWithEnvironmentAsync(audio, _openAlEnvironment, ["--openal", .. mode]).ConfigureAwait(false);
        }
    }

    private Task<string> RunManagedSampleAsync(string project, params string[] arguments)
        => DotNetAsync(["run", "--project", project, "-c", Configuration, "--no-build", "--", .. arguments]);

    private Task<string> RunOpenAlSampleAsync(string project, params string[] arguments)
        => DotNetWithEnvironmentAsync(_openAlEnvironment, ["run", "--project", project, "-c", Configuration, "--no-build", "--", .. arguments]);

    private async Task<string> PublishAotSampleAsync(string project, string directory, string executable, bool strict)
    {
        string output = Path.Combine(ArtifactRoot, directory);
        var arguments = new List<string> { "publish", project, "-c", Configuration, "-p:PublishAot=true", "-o", output };
        if (strict)
        {
            arguments.AddRange(["-p:ILLinkTreatWarningsAsErrors=true", "-p:IlcTreatWarningsAsErrors=true"]);
        }

        await DotNetAsync(arguments.ToArray()).ConfigureAwait(false);
        return Path.Combine(output, executable + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));
    }

    private async Task FormatNativeSourcesAsync(bool fix)
    {
        string output = await ToolAsync("git", "ls-files", "--cached", "--others", "--exclude-standard", "-z", "--",
            "*.c", "*.cc", "*.cpp", "*.cxx", "*.h", "*.hh", "*.hpp", "*.hxx", "*.inl").ConfigureAwait(false);
        foreach (string path in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!File.Exists(Path.Combine(RootDirectory, path)))
            {
                continue;
            }

            string style = "--style=file:" + Path.Combine(RootDirectory, ".clang-format");
            await ToolAsync("clang-format", fix ? [style, "-i", "--", path] : [style, "--dry-run", "--Werror", "--", path]).ConfigureAwait(false);
        }
    }

    private async Task<string> FindMarkdownLintAsync()
    {
        string executable = (await ToolAsync("mise", "which", "markdownlint-cli2").ConfigureAwait(false)).Trim();
        var file = new FileInfo(executable);
        FileSystemInfo? target = file.ResolveLinkTarget(returnFinalTarget: true);
        if (target is not null && target.Extension == ".mjs")
        {
            return target.FullName;
        }

        if (file.Extension == ".mjs")
        {
            return file.FullName;
        }

        DirectoryInfo? directory = file.Directory;
        for (int depth = 0; directory is not null && depth < 5; depth++, directory = directory.Parent)
        {
            foreach (string prefix in new[] { "node_modules", Path.Combine("lib", "node_modules") })
            {
                string entry = Path.Combine(directory.FullName, prefix, "markdownlint-cli2", "markdownlint-cli2-bin.mjs");
                if (File.Exists(entry))
                {
                    return entry;
                }
            }
        }

        throw new FileNotFoundException("Unable to resolve the markdownlint-cli2 Node entry point. Install the repository's pinned development tools.", executable);
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
