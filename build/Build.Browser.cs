namespace Lumyte.Build;

internal sealed partial class Build
{
    private async Task TestWasmAsync()
    {
        foreach (string browserCase in _browserCases.Where(value => (Suite == "browser" || value != "audio") && (BrowserCase is null || BrowserCase == value)))
        {
            if (browserCase == "contracts")
            {
                continue;
            }

            if (browserCase == "audio")
            {
                await TestBrowserAudioAsync().ConfigureAwait(false);
                continue;
            }

            (string project, string verifier, string log) = browserCase switch
            {
                "graphics" => ("Lumyte.Graphics.Browser.Sample", "tools/graphics/verify-browser.mjs", "browser-webgpu.log"),
                "text" => ("Lumyte.Graphics.Text.Parley.Browser.Sample", "samples/Lumyte.Graphics.Text.Parley.Browser.Sample/verify-browser.mjs", "browser-text-webgpu.log"),
                "ui-application" => ("Lumyte.UI.Application.Browser.Sample", "samples/Lumyte.UI.Application.Browser.Sample/verify-browser.mjs", "browser-ui-application.log"),
                "ui-navigation" => ("Lumyte.UI.Navigation.Browser.Sample", "samples/Lumyte.UI.Navigation.Browser.Sample/verify-browser.mjs", "browser-ui-navigation.log"),
                "ui-pages" => ("Lumyte.UI.Pages.Browser.Sample", "samples/Lumyte.UI.Pages.Browser.Sample/verify-browser.mjs", "browser-ui-pages.log"),
                "ui-transitions" => ("Lumyte.UI.Transitions.Browser.Sample", "samples/Lumyte.UI.Transitions.Browser.Sample/verify-browser.mjs", "browser-ui-transitions.log"),
                _ => throw new InvalidOperationException("Unclassified browser case."),
            };
            string output = Path.Combine(ArtifactRoot, "browser", browserCase);
            await DotNetAsync("publish", "samples/" + project, "-c", Configuration, "-o", output).ConfigureAwait(false);
            string root = Path.Combine(output, "wwwroot");
            await CaptureValidationLogAsync(log, () => NodeAsync(verifier, root)).ConfigureAwait(false);
            if (browserCase == "ui-transitions")
            {
                await CaptureValidationLogAsync("browser-ui-transitions-sequential.log", () => NodeAsync(verifier, root, "--sequential")).ConfigureAwait(false);
                await CaptureValidationLogAsync("browser-ui-transitions-live.log", () => NodeAsync(verifier, root, "--same-content")).ConfigureAwait(false);
            }
        }
    }

    private async Task TestBrowserContractsAsync()
    {
        string[] setupContracts = OperatingSystem.IsLinux() ? ["tools/setup/ci-apt.test.mjs"] : [];
        await NodeAsync(["--test", .. setupContracts, "tools/diagnostics/browser-startup.test.mjs", "tools/graphics/browser-gpu.test.mjs", "tools/graphics/browser-gpu-trace.test.mjs", "tools/graphics/browser-capture-trace.test.mjs", "tools/graphics/browser-flight-recorder.test.mjs", "tools/graphics/browser-process-recorder.test.mjs", "tools/graphics/browser-known-timeout.test.mjs", "tools/graphics/browser-timeout-quarantine.test.mjs"]).ConfigureAwait(false);
        await NodeAsync("--test", "tests/Graphics/Lumyte.Graphics.Hosting.Tests/browser-device-loss.test.mjs").ConfigureAwait(false);
        await NodeAsync("--test", "tests/Audio/browser-audio.test.mjs").ConfigureAwait(false);
        await NodeAsync("--test", "tests/Platform/browser-windowing.test.mjs").ConfigureAwait(false);
    }

    private async Task TestBrowserAudioAsync()
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
    }

}
