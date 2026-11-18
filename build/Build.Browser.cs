namespace Lumyte.Build;

internal sealed partial class Build
{
    private async Task TestWasmAsync()
    {
        foreach (string browserCase in _browserCases.Where(value => (Platform == "browser" || Suite == "browser" || value != "audio") && (BrowserCase is null || BrowserCase == value)))
        {
            if (browserCase == "audio")
            {
                await TestBrowserAudioAsync().ConfigureAwait(false);
                continue;
            }

            BrowserPublication publication = _browserPublications.Single(value => value.Case == browserCase);
            string verifier = publication.Verifier;
            string log = publication.Log;
            string root = await PrepareBrowserPublicationAsync(publication).ConfigureAwait(false);
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
        if (NoBuild)
        {
            foreach (BrowserPublication publication in _browserPublications.Where(value => value.Case == "audio"))
            {
                RequireBrowserPublication(publication);
            }
        }

        string environment = Path.Combine(ArtifactRoot, "build-tools", "audio-playwright");
        string python = OperatingSystem.IsWindows() ? Path.Combine(environment, "Scripts", "python.exe") : Path.Combine(environment, "bin", "python");
        if (!File.Exists(python))
        {
            await PythonAsync("-m", "venv", environment).ConfigureAwait(false);
        }

        await ToolWithEnvironmentAsync(python, _isolatedPythonEnvironment, "-m", "pip", "install", "--disable-pip-version-check", "playwright==1.58.0").ConfigureAwait(false);
        foreach (BrowserPublication publication in _browserPublications.Where(value => value.Case == "audio"))
        {
            string root = await PrepareBrowserPublicationAsync(publication).ConfigureAwait(false);
            string log = publication.Log;
            var arguments = new List<string> { "tools/audio/verify_browser.py", root };
            if (Environment.GetEnvironmentVariable("CHROME") is string chrome && !string.IsNullOrWhiteSpace(chrome))
            {
                arguments.AddRange(["--chromium", chrome]);
            }

            await CaptureValidationLogAsync(log, () => ToolWithEnvironmentAsync(python, _isolatedPythonEnvironment, arguments.ToArray())).ConfigureAwait(false);
        }
    }

}
