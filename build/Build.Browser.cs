using Fallout.Common;

namespace Lumyte.Build;

internal sealed partial class Build
{
    private Target SetupWasm => definition => definition
        .Executes(InstallWasmToolsAsync);

    private Target TestWasm => definition => definition
        .DependsOn(SetupWasm)
        .Executes(async () =>
        {
            string graphics = Path.Combine(ArtifactRoot, "browser", "graphics");
            string text = Path.Combine(ArtifactRoot, "browser", "text");
            string application = Path.Combine(ArtifactRoot, "browser", "ui-application");
            string navigation = Path.Combine(ArtifactRoot, "browser", "ui-navigation");
            string pages = Path.Combine(ArtifactRoot, "browser", "ui-pages");
            string transitions = Path.Combine(ArtifactRoot, "browser", "ui-transitions");
            await DotNetAsync("publish", "samples/Lumyte.Graphics.Browser.Sample", "-c", Configuration, "-o", graphics).ConfigureAwait(false);
            await CaptureValidationLogAsync("browser-webgpu.log", () => NodeAsync("tools/graphics/verify-browser.mjs", Path.Combine(graphics, "wwwroot"))).ConfigureAwait(false);
            await DotNetAsync("publish", "samples/Lumyte.Graphics.Text.Parley.Browser.Sample", "-c", Configuration, "-o", text).ConfigureAwait(false);
            await DotNetAsync("publish", "samples/Lumyte.UI.Application.Browser.Sample", "-c", Configuration, "-o", application).ConfigureAwait(false);
            await CaptureValidationLogAsync("browser-ui-application.log", () => NodeAsync("samples/Lumyte.UI.Application.Browser.Sample/verify-browser.mjs", Path.Combine(application, "wwwroot"))).ConfigureAwait(false);
            await DotNetAsync("publish", "samples/Lumyte.UI.Navigation.Browser.Sample", "-c", Configuration, "-o", navigation).ConfigureAwait(false);
            await CaptureValidationLogAsync("browser-ui-navigation.log", () => NodeAsync("samples/Lumyte.UI.Navigation.Browser.Sample/verify-browser.mjs", Path.Combine(navigation, "wwwroot"))).ConfigureAwait(false);
            await DotNetAsync("publish", "samples/Lumyte.UI.Pages.Browser.Sample", "-c", Configuration, "-o", pages).ConfigureAwait(false);
            await CaptureValidationLogAsync("browser-ui-pages.log", () => NodeAsync("samples/Lumyte.UI.Pages.Browser.Sample/verify-browser.mjs", Path.Combine(pages, "wwwroot"))).ConfigureAwait(false);
            await DotNetAsync("publish", "samples/Lumyte.UI.Transitions.Browser.Sample", "-c", Configuration, "-o", transitions).ConfigureAwait(false);
            await CaptureValidationLogAsync("browser-ui-transitions.log", () => NodeAsync("samples/Lumyte.UI.Transitions.Browser.Sample/verify-browser.mjs", Path.Combine(transitions, "wwwroot"))).ConfigureAwait(false);
            await CaptureValidationLogAsync("browser-ui-transitions-sequential.log", () => NodeAsync("samples/Lumyte.UI.Transitions.Browser.Sample/verify-browser.mjs", Path.Combine(transitions, "wwwroot"), "--sequential")).ConfigureAwait(false);
            await CaptureValidationLogAsync("browser-ui-transitions-live.log", () => NodeAsync("samples/Lumyte.UI.Transitions.Browser.Sample/verify-browser.mjs", Path.Combine(transitions, "wwwroot"), "--same-content")).ConfigureAwait(false);
            await CaptureValidationLogAsync("browser-text-webgpu.log", () => NodeAsync("samples/Lumyte.Graphics.Text.Parley.Browser.Sample/verify-browser.mjs", Path.Combine(text, "wwwroot"))).ConfigureAwait(false);
        });

    private Target TestBrowserContracts => definition => definition
        .Executes(async () =>
        {
            await NodeAsync("--test", "tools/graphics/browser-gpu.test.mjs", "tools/graphics/browser-gpu-trace.test.mjs", "tools/graphics/browser-capture-trace.test.mjs", "tools/graphics/browser-flight-recorder.test.mjs", "tools/graphics/browser-process-recorder.test.mjs", "tools/graphics/browser-known-timeout.test.mjs", "tools/graphics/browser-timeout-quarantine.test.mjs").ConfigureAwait(false);
            await NodeAsync("--test", "tests/Graphics/Lumyte.Graphics.Hosting.Tests/browser-device-loss.test.mjs").ConfigureAwait(false);
            await NodeAsync("--test", "tests/Audio/browser-audio.test.mjs").ConfigureAwait(false);
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

}
