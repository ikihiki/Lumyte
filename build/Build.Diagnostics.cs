using Fallout.Common;

namespace Lumyte.Build;

internal sealed partial class Build
{
    [Parameter("Repository whose successful diagnostic gallery artifacts are collected.")]
    public string? GalleryRepository { get; set; }

    private async Task TestDiagnosticsProcessesAsync()
    {
        await PythonAsync("tools/diagnostics/communication-smoke.py", "--dotnet", DotNetExecutable,
            "--configuration", Configuration, "--output", Path.Combine(ArtifactRoot, "test-results", "diagnostics-communication", "processes")).ConfigureAwait(false);
    }

    private async Task TestDiagnosticsUiAsync()
    {
        await DotNetAsync("publish", "src/Diagnostics/Lumyte.Diagnostics.Server", "-c", Configuration, "--no-build", "--no-restore",
            "-o", Path.Combine(ArtifactRoot, "diagnostics-ui-server")).ConfigureAwait(false);
        await ToolWithEnvironmentAsync(NodeExecutable, new Dictionary<string, string?>
        {
            ["DOTNET"] = DotNetExecutable,
            ["LUMYTE_CONFIGURATION"] = Configuration,
        }, "tools/diagnostics/verify-browser.mjs").ConfigureAwait(false);
    }

    private async Task GenerateDiagnosticGalleryAsync()
    {
        await NodeAsync("tools/diagnostics/generate-gallery.test.mjs").ConfigureAwait(false);
        await NodeAsync("tools/diagnostics/assemble-gallery-site.test.mjs").ConfigureAwait(false);
        await PythonAsync("-m", "unittest", "discover", "-s", "tools/diagnostics", "-p", "test_collect_gallery_artifacts.py").ConfigureAwait(false);
        await NodeAsync("tools/diagnostics/generate-gallery.mjs", Path.Combine(ArtifactRoot, "test-results", "diagnostics-ui"),
            Path.Combine(ArtifactRoot, "diagnostics-gallery")).ConfigureAwait(false);
    }

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

}
