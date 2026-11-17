using System.Text.Json;
using Xunit;

namespace Lumyte.Build.Tests;

/// <summary>Public task options replace feature and CI alias targets without losing their scenarios.</summary>
public sealed class TaskSelectionTests
{
    /// <summary>The real help surface cannot re-expand through aliases.</summary>
    /// <returns>The completed inventory assertion.</returns>
    [Fact]
    [Trait("Execution", "Integration")]
    public async Task ActualTargetInventoryCannotReexpandAsync()
    {
        using var fixture = new BuildFixture();
        BuildFixture.ProcessResult result = await fixture.RunAsync("Test", "--help");
        Assert.True(result.ExitCode == 0, result.Output + result.Error);
        string targets = result.Output.Split("Parameters", StringSplitOptions.None)[0];
        string[] names = System.Text.RegularExpressions.Regex.Matches(targets, @"^  ([A-Za-z][A-Za-z0-9]+)(?: +|$)", System.Text.RegularExpressions.RegexOptions.Multiline)
            .Select(match => match.Groups[1].Value).ToArray();
        Assert.Equal(18, names.Length);
        Assert.Equal(["Test"], names.Where(name => name.StartsWith("Test", StringComparison.Ordinal)));
        Assert.DoesNotContain(names, name => name.StartsWith("Ci", StringComparison.Ordinal) && name != "Ci");
        Assert.Contains("Benchmark", names);
        Assert.DoesNotContain("Ci", names);
        Assert.DoesNotContain("PrepareTest", names);
        Assert.DoesNotContain("ValidateTest", names);
        Assert.Contains("--lane", result.Output, StringComparison.Ordinal);
        Assert.Contains("--suite", result.Output, StringComparison.Ordinal);
    }

    /// <summary>Invalid or incompatible selectors fail before restore, build, or test launches.</summary>
    /// <param name="target">Task entry point.</param>
    /// <param name="arguments">Invalid selection.</param>
    /// <returns>The completed assertion.</returns>
    [Theory]
    [InlineData("Distribution", "--distribution-output", "pack-typo")]
    [InlineData("Quality", "--quality-scope", "typo")]
    [InlineData("Format", "--quality-scope", "typo")]
    [InlineData("Native", "--native-backend", "typo")]
    [InlineData("Setup", "--setup-tool", "typo")]
    [InlineData("Verify", "--verify-scope", "typo")]
    [InlineData("Test", "--pipeline-validation")]
    [InlineData("Test", "--lane", "cp")]
    [InlineData("Test", "--suite", "browser-typo")]
    [InlineData("Test", "--suite", "ui")]
    [InlineData("Test", "--suite", "ui", "--feature", "tab")]
    [InlineData("Test", "--feature", "tabs")]
    [InlineData("Test", "--suite", "ui", "--feature", "media", "--aot")]
    [InlineData("Test", "--suite", "ui", "--feature", "tabs", "--lane", "cpu")]
    [InlineData("Test", "--test-concurrency", "0")]
    [InlineData("Test", "--test-concurrency", "65")]
    [InlineData("Benchmark", "--benchmark-area", "grapics")]
    [InlineData("Benchmark", "--benchmark-area", "core", "--benchmark-integrated")]
    [InlineData("Benchmark", "--benchmark-area", "ui", "--benchmark-evaluate")]
    [Trait("Execution", "Integration")]
    public async Task InvalidSelectionLaunchesNoToolsAsync(string target, params string[] arguments)
    {
        using var fixture = new BuildFixture();
        BuildFixture.ProcessResult result = await fixture.RunAsync(target, [.. arguments, "--dotnet-path", fixture.DotnetTool]);
        Assert.NotEqual(0, result.ExitCode);
        Assert.False(File.Exists(Path.Combine(fixture.Log, "dotnet.jsonl")), result.Output + result.Error);
    }

    /// <summary>Every formerly independent UI gate retains its exact suite and sample list.</summary>
    /// <param name="feature">Feature selector.</param>
    /// <param name="projects">Expected unit projects.</param>
    /// <param name="samples">Expected public samples.</param>
    /// <returns>The completed assertion.</returns>
    [Theory]
    [InlineData("layout", "UI", "Layout")]
    [InlineData("controls", "UI.Controls", "Controls")]
    [InlineData("media", "UI.Media,UI.Media.Parley", "Media")]
    [InlineData("scroll", "UI,UI.Controls", "Scroll")]
    [InlineData("menus", "UI.Controls,UI.Windowing", "Menus")]
    [InlineData("tabs", "UI.Controls,UI.TextEditing", "Tabs")]
    [InlineData("collections", "UI.Collections", "Collections")]
    [InlineData("numeric", "UI.Controls,UI.TextEditing,UI.Automation", "Numeric")]
    [InlineData("application", "UI.Application", "Application.Headless")]
    [InlineData("navigation", "UI.Navigation", "Navigation")]
    [InlineData("pages", "UI.Application.Navigation", "Pages,Transitions")]
    [Trait("Execution", "Integration")]
    public async Task FeatureRetainsExactScenariosAsync(string feature, string projects, string samples)
    {
        using var fixture = new BuildFixture();
        BuildFixture.ProcessResult result = await fixture.RunAsync("Test", "--suite", "ui", "--feature", feature, "--no-build", "--dotnet-path", fixture.DotnetTool);
        Assert.True(result.ExitCode == 0, result.Output + result.Error);
        JsonElement[] records = fixture.Records("dotnet");
        string[][] calls = records.Select(record => record.GetProperty("arguments").EnumerateArray().Select(value => value.GetString()!).ToArray()).ToArray();
        Assert.Equal(projects.Split(',').Select(name => "tests/UI/Lumyte." + name + ".Tests"), calls.Where(call => call[0] == "test").Select(call => call[1]));
        Assert.Equal(samples.Split(',').Select(name => "samples/Lumyte.UI." + name + ".Sample"), calls.Where(call => call[0] == "run").Select(call => call[Array.IndexOf(call, "--project") + 1]));
        Assert.Equal(projects.Split(',').Length + samples.Split(',').Length, calls.Length);
        if (feature is "controls" or "media" or "scroll" or "menus" or "tabs" or "collections" or "numeric")
        {
            Assert.All(calls.Where(call => call[0] == "run"), call => Assert.Contains("tests/Graphics/Lumyte.Graphics.Text.Parley.Tests/Fonts/DejaVuSans.ttf", call));
        }
    }

    /// <summary>All seven AOT feature scenarios retain strict publication and fail on child errors.</summary>
    /// <param name="feature">Feature selector.</param>
    /// <returns>The completed assertion.</returns>
    [Theory]
    [InlineData("layout")]
    [InlineData("controls")]
    [InlineData("scroll")]
    [InlineData("menus")]
    [InlineData("tabs")]
    [InlineData("collections")]
    [InlineData("numeric")]
    [Trait("Execution", "Integration")]
    public async Task AotFeatureRetainsStrictPublicationAsync(string feature)
    {
        using var fixture = new BuildFixture();
        fixture.Environment["LUMYTE_TEST_DOTNET_FAIL_COMMAND"] = "publish";
        fixture.Environment["LUMYTE_TEST_DOTNET_EXIT"] = "23";
        BuildFixture.ProcessResult result = await fixture.RunAsync("Test", "--suite", "ui", "--feature", feature, "--aot", "--dotnet-path", fixture.DotnetTool);
        Assert.NotEqual(0, result.ExitCode);
        JsonElement record = Assert.Single(fixture.Records("dotnet"));
        string[] args = record.GetProperty("arguments").EnumerateArray().Select(value => value.GetString()!).ToArray();
        Assert.Equal("publish", args[0]);
        Assert.Contains("-p:ILLinkTreatWarningsAsErrors=true", args);
        Assert.Contains("-p:IlcTreatWarningsAsErrors=true", args);
        Assert.Contains("-m:1", args);
    }
}
