using System.Text.Json;
using Xunit;

namespace Lumyte.Build.Tests;

/// <summary>Isolated Browser jobs partition the complete gate, not its assertions.</summary>
public sealed class BrowserCaseTests
{
    /// <summary>Every selected case runs precisely its publication and verifier contract.</summary>
    /// <param name="selection">The exact independent case, or the complete suite.</param>
    /// <returns>The completed process assertions.</returns>
    [Theory]
    [InlineData(null)]
    [InlineData("contracts")]
    [InlineData("graphics")]
    [InlineData("text")]
    [InlineData("ui-application")]
    [InlineData("ui-navigation")]
    [InlineData("ui-pages")]
    [InlineData("ui-transitions")]
    [InlineData("audio")]
    [Trait("Execution", "Integration")]
    public async Task BrowserCasesPreserveExactCoverageAsync(string? selection)
    {
        using var fixture = new BuildFixture();
        fixture.Environment["LUMYTE_TEST_QUALITY_TOOLS"] = "1";
        File.WriteAllText(Path.Combine(fixture.Root, "global.json"), "{\"sdk\":{\"version\":\"10.0.403\"}}");
        StageAudioPython(fixture);

        string[] selected = selection is null ? [] : ["--browser-case", selection];
        BuildFixture.ProcessResult result = await fixture.RunAsync("Test", ["--suite", "browser", .. selected, "--dotnet-path", fixture.DotnetTool, "--node", fixture.Node, "--python", fixture.Python]);
        Assert.True(result.ExitCode == 0, result.Output + result.Error);
        string[][] dotnet = Records(fixture, "dotnet").Select(Arguments).ToArray();
        string[] expectedProjects = new (string Case, string Project)[]
        {
            ("graphics", "Lumyte.Graphics.Browser.Sample"),
            ("text", "Lumyte.Graphics.Text.Parley.Browser.Sample"),
            ("ui-application", "Lumyte.UI.Application.Browser.Sample"),
            ("ui-navigation", "Lumyte.UI.Navigation.Browser.Sample"),
            ("ui-pages", "Lumyte.UI.Pages.Browser.Sample"),
            ("ui-transitions", "Lumyte.UI.Transitions.Browser.Sample"),
            ("audio", "Lumyte.Audio.Execution.Browser.Sample"),
            ("audio", "Lumyte.Audio.Browser.Sample"),
        }.Where(value => selection is null || selection == value.Case).Select(value => "samples/" + value.Project).ToArray();
        Assert.Equal(expectedProjects, dotnet.Where(call => call[0] == "publish").Select(call => call[1]));
        Assert.All(dotnet.Where(call => call[0] == "publish"), call => Assert.Contains("-m:1", call));
        Assert.Equal(selection == "contracts" ? 0 : 1, dotnet.Count(call => call[0] == "workload"));
        string[][] nodes = Records(fixture, "node").Select(Arguments).ToArray();
        Assert.Equal(selection is null or "contracts" ? 4 : 0, nodes.Count(call => call[0] == "--test"));
        string[] expectedVerifiers = expectedProjects.Where(project => !project.Contains("Audio", StringComparison.Ordinal))
            .Select(project => project == "samples/Lumyte.Graphics.Browser.Sample" ? "tools/graphics/verify-browser.mjs" : project + "/verify-browser.mjs").ToArray();
        string[][] verifies = nodes.Where(call => call[0] != "--test").ToArray();
        Assert.Equal(expectedVerifiers, verifies.Where(call => call.Length == 2).Select(call => call[0]));
        Assert.Equal(selection is null or "ui-transitions" ? new[] { "--sequential", "--same-content" } : [], verifies.Where(call => call.Length == 3).Select(call => call[2]));
        Assert.Equal(selection is null or "audio" ? 2 : 0, Records(fixture, "python").Count(record => Arguments(record)[0] == "tools/audio/verify_browser.py"));
    }

    /// <summary>An invalid shard or incompatible suite cannot install workloads or start a child tool.</summary>
    /// <param name="suite">The requested suite.</param>
    /// <param name="selection">Invalid or incompatible selector.</param>
    /// <returns>The completed no-side-effects assertion.</returns>
    [Theory]
    [InlineData("browser", "")]
    [InlineData("browser", "Graphics")]
    [InlineData("browser", "ui-transition")]
    [InlineData("browser", "graphics,text")]
    [InlineData("managed", "graphics")]
    [InlineData("wasm", "graphics")]
    [InlineData("browser-audio", "audio")]
    [Trait("Execution", "Integration")]
    public async Task InvalidBrowserCaseStartsNoToolsAsync(string suite, string selection)
    {
        using var fixture = new BuildFixture();
        BuildFixture.ProcessResult result = await fixture.RunAsync("Test", "--suite", suite, "--browser-case", selection, "--dotnet-path", fixture.DotnetTool);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Empty(Records(fixture, "dotnet"));
    }

    /// <summary>A failed publish stops the case before browser assertions can report success.</summary>
    /// <param name="selection">The independent case.</param>
    /// <returns>The completed failure propagation assertion.</returns>
    [Theory]
    [InlineData("graphics")]
    [InlineData("text")]
    [InlineData("ui-application")]
    [InlineData("ui-navigation")]
    [InlineData("ui-pages")]
    [InlineData("ui-transitions")]
    [InlineData("audio")]
    [Trait("Execution", "Integration")]
    public async Task PublishFailureCannotPassBrowserCaseAsync(string selection)
    {
        using var fixture = new BuildFixture();
        File.WriteAllText(Path.Combine(fixture.Root, "global.json"), """{"sdk":{"version":"10.0.403"}}""");
        StageAudioPython(fixture);
        fixture.Environment["LUMYTE_TEST_DOTNET_FAIL_COMMAND"] = "publish";
        fixture.Environment["LUMYTE_TEST_DOTNET_EXIT"] = "23";
        BuildFixture.ProcessResult result = await fixture.RunAsync("Test", "--suite", "browser", "--browser-case", selection, "--dotnet-path", fixture.DotnetTool, "--node", fixture.Node, "--python", fixture.Python);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Single(Records(fixture, "dotnet"), record => Arguments(record)[0] == "publish");
        Assert.Empty(Records(fixture, "node"));
    }

    private static JsonElement[] Records(BuildFixture fixture, string tool)
        => File.Exists(Path.Combine(fixture.Log, tool + ".jsonl")) ? fixture.Records(tool) : [];

    private static void StageAudioPython(BuildFixture fixture)
    {
        string audioTools = Path.Combine(fixture.Root, "artifacts", "build-tools", "audio-playwright", OperatingSystem.IsWindows() ? "Scripts" : "bin");
        Directory.CreateDirectory(audioTools);
        foreach (string file in Directory.GetFiles(fixture.Tools))
        {
            File.Copy(file, Path.Combine(audioTools, Path.GetFileName(file)));
        }

        string python = Path.Combine(audioTools, OperatingSystem.IsWindows() ? "python.exe" : "python");
        File.Copy(fixture.Python, python);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(python, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static string[] Arguments(JsonElement record) => record.GetProperty("arguments").EnumerateArray().Select(value => value.GetString()!).ToArray();
}
