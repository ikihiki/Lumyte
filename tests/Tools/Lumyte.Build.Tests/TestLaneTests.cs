using System.Text.Json;
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
        Assert.Contains(expected, arguments[Array.IndexOf(arguments, "--filter") + 1], StringComparison.Ordinal);
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
        File.WriteAllText(fixture.Solution, $"<Solution><Project Path=\"{Path.GetFileName(fixture.Project)}\" /></Solution>");
        Directory.CreateDirectory(Path.Combine(fixture.Root, "build"));
        File.WriteAllText(Path.Combine(fixture.Root, "build", "TestLanes.json"), JsonSerializer.Serialize(new[]
        {
            new { Project = Path.GetFileName(fixture.Project), Default = "Cpu", IntegrationClasses = new[] { "ExternalTests" } },
        }));
    }
}
