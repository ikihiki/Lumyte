namespace Lumyte.Build;

internal sealed partial class Build
{
    private sealed record BrowserPublication(string Case, string Project, string Directory, string Verifier, string Log);

    // Seven API cases require eight applications: audio exercises both execution
    // and resource hosting. Keep both publications and their independent checks.
    private static readonly BrowserPublication[] _browserPublications =
    [
        new("graphics", "Lumyte.Graphics.Browser.Sample", "browser/graphics", "tools/graphics/verify-browser.mjs", "browser-webgpu.log"),
        new("text", "Lumyte.Graphics.Text.Parley.Browser.Sample", "browser/text", "samples/Lumyte.Graphics.Text.Parley.Browser.Sample/verify-browser.mjs", "browser-text-webgpu.log"),
        new("ui-application", "Lumyte.UI.Application.Browser.Sample", "browser/ui-application", "samples/Lumyte.UI.Application.Browser.Sample/verify-browser.mjs", "browser-ui-application.log"),
        new("ui-navigation", "Lumyte.UI.Navigation.Browser.Sample", "browser/ui-navigation", "samples/Lumyte.UI.Navigation.Browser.Sample/verify-browser.mjs", "browser-ui-navigation.log"),
        new("ui-pages", "Lumyte.UI.Pages.Browser.Sample", "browser/ui-pages", "samples/Lumyte.UI.Pages.Browser.Sample/verify-browser.mjs", "browser-ui-pages.log"),
        new("ui-transitions", "Lumyte.UI.Transitions.Browser.Sample", "browser/ui-transitions", "samples/Lumyte.UI.Transitions.Browser.Sample/verify-browser.mjs", "browser-ui-transitions.log"),
        new("audio", "Lumyte.Audio.Execution.Browser.Sample", "audio-execution-browser", "tools/audio/verify_browser.py", "browser-audio-execution.log"),
        new("audio", "Lumyte.Audio.Browser.Sample", "audio-browser", "tools/audio/verify_browser.py", "browser-audio.log"),
    ];

    // Compile --build-kind browser calls this producer, not the test runner.
    private async Task PublishBrowserAsync()
    {
        if (NoBuild)
        {
            foreach (BrowserPublication publication in _browserPublications)
            {
                RequireBrowserPublication(publication);
            }

            return;
        }

        await InstallWasmToolsAsync().ConfigureAwait(false);
        foreach (BrowserPublication publication in _browserPublications)
        {
            await PrepareBrowserPublicationAsync(publication).ConfigureAwait(false);
        }
    }

    private async Task<string> PrepareBrowserPublicationAsync(BrowserPublication publication)
    {
        string output = Path.Combine(ArtifactRoot, publication.Directory);
        if (!NoBuild)
        {
            var arguments = new List<string> { "publish", "samples/" + publication.Project, "-c", Configuration, "-o", output };
            if (NoRestore)
            {
                arguments.Add("--no-restore");
            }

            await DotNetAsync(arguments.ToArray()).ConfigureAwait(false);
        }

        return RequireBrowserPublication(publication);
    }

    private string RequireBrowserPublication(BrowserPublication publication)
    {
        string root = Path.Combine(ArtifactRoot, publication.Directory, "wwwroot");
        RequirePublishedFile(Path.Combine(root, "index.html"), "browser");
        string framework = Path.Combine(root, "_framework");
        if (!Directory.Exists(framework) || !Directory.EnumerateFiles(framework, "*.wasm", SearchOption.AllDirectories).Any())
        {
            throw new FileNotFoundException($"Missing browser WASM payload in {framework}. Run Compile --build-kind browser before Test --no-build.");
        }

        return root;
    }

    // Compile --build-kind aot publishes every project exactly once, independent
    // of consumer group selection. All producer publications are strict.
    private async Task PublishAotAsync()
    {
        foreach (AotSample sample in AotSamples)
        {
            if (!NoBuild)
            {
                await PublishAotSampleAsync(sample.Project, sample.Directory, sample.Executable, strict: true).ConfigureAwait(false);
            }

            RequireAotPublication(sample);
        }
    }

    private string RequireAotPublication(AotSample sample)
    {
        string executable = Path.Combine(ArtifactRoot, sample.Directory, sample.Executable + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));
        RequirePublishedFile(executable, "aot");
        return executable;
    }

    private static void RequirePublishedFile(string path, string kind)
    {
        if (!File.Exists(path) || new FileInfo(path).Length == 0)
        {
            throw new FileNotFoundException($"Missing or empty published artifact {path}. Run Compile --build-kind {kind} before Test --no-build.", path);
        }
    }
}
