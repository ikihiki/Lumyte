using System.Text.Json;
using System.Xml.Linq;
using Xunit;

namespace Lumyte.Build.Tests;

/// <summary>Exercises lane entry points through real Fallout and controlled child tools.</summary>
public sealed class TestLaneTests
{
    /// <summary>Each tier carries an exclusive test filter and isolated result directory.</summary>
    /// <param name="lane">The public entry point.</param>
    /// <param name="expected">The selected filter fragment.</param>
    /// <returns>The completed process assertion.</returns>
    [Theory]
    [InlineData("cpu", "Execution!=Integration")]
    [InlineData("integration", "Execution=Integration")]
    [InlineData("gpu", "Execution=Gpu")]
    [Trait("Execution", "Integration")]
    public async Task LaneSelectsExclusiveFilterAsync(string lane, string expected)
    {
        if (lane == "cpu" && (!OperatingSystem.IsLinux() || System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64))
        {
            return;
        }

        using var fixture = new BuildFixture();
        PrepareCatalog(fixture);
        fixture.Environment["LUMYTE_CPU_METHOD_PARALLELISM"] = "63";
        fixture.Environment["LUMYTE_GRAPHICS_GPU_TESTS"] = "1";
        fixture.Environment["LUMYTE_OPENAL_TESTS"] = "1";
        fixture.Environment["LUMYTE_UI_BENCHMARK_GPU"] = "1";
        BuildFixture.ProcessResult result = await fixture.RunAsync("Test", "--lane", lane, "--dotnet-path", fixture.DotnetTool, "--no-build");
        Assert.Equal(0, result.ExitCode);
        JsonElement record = Assert.Single(fixture.Records("dotnet"));
        string[] arguments = record.GetProperty("arguments").EnumerateArray().Select(value => value.GetString()!).ToArray();
        Assert.Equal("test", arguments[0]);
        Assert.EndsWith(".slnf", arguments[1], StringComparison.Ordinal);
        Assert.DoesNotContain("--filter", arguments);
        using var solutionFilter = JsonDocument.Parse(File.ReadAllText(arguments[1]));
        JsonElement solution = solutionFilter.RootElement.GetProperty("solution");
        Assert.Equal(fixture.Solution, Path.GetFullPath(solution.GetProperty("path").GetString()!, Path.GetDirectoryName(arguments[1])!));
        string[] members = solution.GetProperty("projects").EnumerateArray().Select(value => Path.GetFullPath(value.GetString()!, fixture.Root)).ToArray();
        Assert.Equal(lane == "cpu" ? [fixture.Project] : [fixture.Project, Path.Combine(fixture.Root, "Native Fixture.csproj")], members);
        string targetsFile = Assert.Single(arguments, argument => argument.StartsWith("-p:LumyteTestLaneTargets=", StringComparison.Ordinal))["-p:LumyteTestLaneTargets=".Length..];
        var targets = XDocument.Load(targetsFile);
        XElement[] groups = targets.Root!.Elements("PropertyGroup").Where(group => group.Element("LumyteLaneSelected")?.Value == "true").ToArray();
        Assert.Equal(members.Length, groups.Length);
        Assert.Contains(expected, groups[0].Element("VSTestTestCaseFilter")!.Value, StringComparison.Ordinal);
        if (lane == "integration")
        {
            Assert.Equal("Execution!=Gpu", groups[1].Element("VSTestTestCaseFilter")!.Value);
            Assert.Contains("FullyQualifiedName~.ExternalTests.", groups[0].Element("VSTestTestCaseFilter")!.Value, StringComparison.Ordinal);
        }

        Assert.All(groups, group => Assert.Equal("trx%3bLogFileName=$(MSBuildProjectName).trx", group.Element("VSTestLogger")!.Value));
        Assert.Contains("--no-build", arguments);
        Assert.Contains("--no-restore", arguments);
        Assert.Equal(lane == "cpu" ? null : "1", record.GetProperty("environment").GetProperty("LUMYTE_TEST_SERIAL").GetString());
        Assert.Equal(lane == "cpu" ? "4" : null, record.GetProperty("environment").GetProperty("LUMYTE_CPU_METHOD_PARALLELISM").GetString());
        if (lane == "cpu")
        {
            foreach (string key in new[] { "LUMYTE_GRAPHICS_GPU_TESTS", "LUMYTE_OPENAL_TESTS", "LUMYTE_UI_BENCHMARK_GPU" })
            {
                Assert.Equal(JsonValueKind.Null, record.GetProperty("environment").GetProperty(key).ValueKind);
            }
        }
    }

    /// <summary>A new test project must be classified before any test process starts.</summary>
    /// <returns>The completed fail-closed assertion.</returns>
    [Fact]
    [Trait("Execution", "Integration")]
    public async Task UnknownProjectFailsBeforeTestLaunchAsync()
    {
        using var fixture = new BuildFixture();
        PrepareCatalog(fixture);
        File.WriteAllText(Path.Combine(fixture.Root, "build", "TestLanes.json"), "[]");
        BuildFixture.ProcessResult result = await fixture.RunAsync("Test", "--lane", "integration", "--dotnet-path", fixture.DotnetTool, "--no-build");
        Assert.NotEqual(0, result.ExitCode);
        Assert.False(File.Exists(Path.Combine(fixture.Log, "dotnet.jsonl")));
        Assert.Contains("Classify", result.Output + result.Error, StringComparison.Ordinal);
    }

    private static void PrepareCatalog(BuildFixture fixture)
    {
        File.WriteAllText(fixture.Project, "<Project><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(fixture.Root, "Native Fixture.csproj"), "<Project><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>");
        File.WriteAllText(fixture.Solution, $"<Solution><Project Path=\"{Path.GetFileName(fixture.Project)}\" /><Project Path=\"Native Fixture.csproj\" /></Solution>");
        Directory.CreateDirectory(Path.Combine(fixture.Root, "build"));
        File.WriteAllText(Path.Combine(fixture.Root, "build", "TestLanes.json"), JsonSerializer.Serialize(new[]
        {
            new { Project = Path.GetFileName(fixture.Project), Default = "Cpu", IntegrationClasses = new[] { "ExternalTests" } },
            new { Project = "Native Fixture.csproj", Default = "Integration", IntegrationClasses = Array.Empty<string>() },
        }));
    }
}
