using System.Text.Json;
using Xunit;

namespace Lumyte.Build.Tests;

/// <summary>Balanced independent AOT groups retain the complete publish and executable manifest.</summary>
public sealed class AotGroupTests
{
    private static readonly (string Name, int Group, bool Strict, int Seconds)[] _manifest =
    [
        ("Execution.Sample", 1, false, 14), ("Signals.Sample", 1, true, 7),
        ("Resources.AotSmoke", 1, false, 17), ("Resources.Graphics.AotSmoke", 1, true, 42),
        ("UI.Application.Headless.Sample", 2, true, 98), ("UI.Navigation.Sample", 1, true, 88),
        ("UI.Layout.Sample", 2, true, 87), ("UI.Functions.Sample", 1, true, 72),
        ("UI.Media.Sample", 1, true, 124), ("UI.Controls.Sample", 2, true, 89),
        ("UI.Collections.Sample", 2, true, 96), ("UI.Pages.Sample", 1, true, 86),
        ("UI.Transitions.Sample", 2, true, 87), ("Resources.Audio.IntegrationTests", 1, true, 11),
    ];

    /// <summary>Omission and each group execute every expected project, strict flag and invocation mode.</summary>
    /// <param name="group">The selected group, or the complete suite.</param>
    /// <returns>The completed manifest assertion.</returns>
    [Theory]
    [InlineData(null)]
    [InlineData("balanced-1")]
    [InlineData("balanced-2")]
    [Trait("Execution", "Integration")]
    public async Task ExactManifestAsync(string? group)
    {
        using var fixture = new BuildFixture();
        fixture.Environment["LUMYTE_TEST_STAGE_AOT"] = "1";
        fixture.Environment["ALSOFT_DRIVERS"] = null;
        BuildFixture.ProcessResult result = await RunAsync(fixture, group);
        Assert.True(result.ExitCode == 0, result.Output + result.Error);
        (string Name, int Group, bool Strict, int Seconds)[] selected = _manifest.Where(item => group is null || group == "balanced-" + item.Group).ToArray();
        JsonElement[] publishes = fixture.Records("dotnet");
        Assert.Equal(selected.Select(item => Project(item.Name)), publishes.Select(record => Arguments(record)[1]));
        foreach ((string Name, int Group, bool Strict, int Seconds) item in selected)
        {
            string name = "Lumyte." + item.Name;
            string[] publish = Arguments(Assert.Single(publishes, record => Arguments(record)[1] == Project(item.Name)));
            Assert.Equal("publish", publish[0]);
            Assert.Contains("-p:PublishAot=true", publish);
            Assert.Contains("-m:1", publish);
            Assert.Equal(item.Strict, publish.Contains("-p:ILLinkTreatWarningsAsErrors=true", StringComparer.Ordinal));
            Assert.Equal(item.Strict, publish.Contains("-p:IlcTreatWarningsAsErrors=true", StringComparer.Ordinal));
            string[][] modes = item.Name switch
            {
                "Execution.Sample" => [[], ["--di"], ["--host"], ["--host", "--di"]],
                "Resources.AotSmoke" or "Resources.Graphics.AotSmoke" => [[], ["--host"]],
                "Resources.Audio.IntegrationTests" => [[], ["--openal"], ["--di"], ["--openal", "--di"], ["--host"], ["--openal", "--host"], ["--host", "--di"], ["--openal", "--host", "--di"]],
                "UI.Media.Sample" or "UI.Controls.Sample" or "UI.Collections.Sample" => [["tests/Graphics/Lumyte.Graphics.Text.Parley.Tests/Fonts/DejaVuSans.ttf"]],
                _ => [[]],
            };
            JsonElement[] calls = fixture.Records(name);
            Assert.Equal(modes.Select(mode => string.Join("|", mode)), calls.Select(call => string.Join("|", Arguments(call))));
            Assert.All(calls, call => Assert.Equal(Arguments(call).Contains("--openal", StringComparer.Ordinal) ? "null" : null, call.GetProperty("environment").GetProperty("ALSOFT_DRIVERS").GetString()));
        }

        string[] expectedLogs = selected.Select(item => "Lumyte." + item.Name + ".jsonl").Append("dotnet.jsonl").Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(expectedLogs, Directory.GetFiles(fixture.Log, "*.jsonl").Select(Path.GetFileName).Order(StringComparer.Ordinal));
    }

    /// <summary>The frozen groups are an exact disjoint union and mix measured expensive and cheap publications.</summary>
    [Fact]
    public void GroupsAreBalancedDisjointComplete()
    {
        Assert.Equal(14, _manifest.Length);
        Assert.Equal(14, _manifest.Select(item => item.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(9, _manifest.Count(item => item.Group == 1));
        Assert.Equal(5, _manifest.Count(item => item.Group == 2));
        Assert.InRange(Math.Abs(_manifest.Where(item => item.Group == 1).Sum(item => item.Seconds) - _manifest.Where(item => item.Group == 2).Sum(item => item.Seconds)), 0, 10);
    }

    /// <summary>Invalid and incompatible selectors cannot start child tools.</summary>
    /// <param name="suite">The requested suite.</param>
    /// <param name="group">The invalid selector.</param>
    /// <returns>The completed preflight assertion.</returns>
    [Theory]
    [InlineData("aot", "")]
    [InlineData("aot", "Balanced-1")]
    [InlineData("aot", "balanced-3")]
    [InlineData("aot", "balanced-1,balanced-2")]
    [InlineData("managed", "balanced-1")]
    [InlineData("browser", "balanced-2")]
    [Trait("Execution", "Integration")]
    public async Task InvalidSelectionStartsNoToolsAsync(string suite, string group)
    {
        using var fixture = new BuildFixture();
        BuildFixture.ProcessResult result = await fixture.RunAsync("Test", "--suite", suite, "--aot-group", group, "--dotnet-path", fixture.DotnetTool);
        Assert.NotEqual(0, result.ExitCode);
        Assert.False(Directory.Exists(fixture.Log));
    }

    /// <summary>Each group's failed publish or executable stops remaining work and fails the gate.</summary>
    /// <param name="group">The independent group.</param>
    /// <param name="executableFailure">Whether to fail the executable instead of publication.</param>
    /// <returns>The completed propagation assertion.</returns>
    [Theory]
    [InlineData("balanced-1", false)]
    [InlineData("balanced-2", false)]
    [InlineData("balanced-1", true)]
    [InlineData("balanced-2", true)]
    [Trait("Execution", "Integration")]
    public async Task FailuresPropagateAsync(string group, bool executableFailure)
    {
        using var fixture = new BuildFixture();
        fixture.Environment["LUMYTE_TEST_STAGE_AOT"] = "1";
        if (executableFailure)
        {
            fixture.Environment["LUMYTE_TEST_AOT_FAIL_EXECUTABLE"] = group == "balanced-1" ? "Lumyte.Execution.Sample" : "Lumyte.UI.Application.Headless.Sample";
        }
        else
        {
            fixture.Environment["LUMYTE_TEST_DOTNET_FAIL_COMMAND"] = "publish";
            fixture.Environment["LUMYTE_TEST_DOTNET_EXIT"] = "23";
        }

        BuildFixture.ProcessResult result = await RunAsync(fixture, group);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Single(fixture.Records("dotnet"));
        Assert.Equal(executableFailure ? 2 : 1, Directory.GetFiles(fixture.Log, "*.jsonl").Length);
    }

    private static Task<BuildFixture.ProcessResult> RunAsync(BuildFixture fixture, string? group)
        => fixture.RunAsync("Test", ["--suite", "aot", "--dotnet-path", fixture.DotnetTool, .. group is null ? Array.Empty<string>() : ["--aot-group", group]]);

    private static string Project(string name) => name switch
    {
        "Resources.AotSmoke" => "tests/Core/Lumyte." + name,
        "Resources.Graphics.AotSmoke" => "tests/Graphics/Lumyte." + name,
        "Resources.Audio.IntegrationTests" => "tests/Audio/Lumyte." + name,
        _ => "samples/Lumyte." + name,
    };

    private static string[] Arguments(JsonElement record) => record.GetProperty("arguments").EnumerateArray().Select(value => value.GetString()!).ToArray();
}
