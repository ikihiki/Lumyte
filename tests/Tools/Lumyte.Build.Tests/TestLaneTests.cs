using System.Text.Json;
using Xunit;

namespace Lumyte.Build.Tests;

/// <summary>Exercises lane entry points through real Fallout and controlled child tools.</summary>
public sealed class TestLaneTests
{
    /// <summary>Each tier carries an exclusive test filter and isolated result directory.</summary>
    /// <param name="target">The public entry point.</param>
    /// <param name="expected">The selected filter fragment.</param>
    /// <returns>The completed process assertion.</returns>
    [Theory]
    [InlineData("TestCpu", "Execution!=Integration")]
    [InlineData("CiCpu", "Execution!=Integration")]
    [InlineData("TestIntegration", "Execution=Integration")]
    [InlineData("CiIntegration", "Execution=Integration")]
    [InlineData("CiNative", "Execution=Integration")]
    [InlineData("TestGpu", "Execution=Gpu")]
    [InlineData("CiGpu", "Execution=Gpu")]
    [Trait("Execution", "Integration")]
    public async Task LaneSelectsExclusiveFilterAsync(string target, string expected)
    {
        if (target.EndsWith("Cpu", StringComparison.Ordinal) && (!OperatingSystem.IsLinux() || System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64))
        {
            return;
        }

        using var fixture = new BuildFixture();
        PrepareCatalog(fixture);
        fixture.Environment["LUMYTE_GRAPHICS_GPU_TESTS"] = "1";
        fixture.Environment["LUMYTE_OPENAL_TESTS"] = "1";
        fixture.Environment["LUMYTE_UI_BENCHMARK_GPU"] = "1";
        BuildFixture.ProcessResult result = await fixture.RunAsync(target, "--dotnet-path", fixture.DotnetTool, "--no-build");
        Assert.Equal(0, result.ExitCode);
        JsonElement record = Assert.Single(fixture.Records("dotnet"));
        string[] arguments = record.GetProperty("arguments").EnumerateArray().Select(value => value.GetString()!).ToArray();
        Assert.Contains(expected, arguments[Array.IndexOf(arguments, "--filter") + 1], StringComparison.Ordinal);
        Assert.Contains("--no-build", arguments);
        Assert.Contains("--no-restore", arguments);
        if (target.EndsWith("Cpu", StringComparison.Ordinal))
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
        BuildFixture.ProcessResult result = await fixture.RunAsync("TestIntegration", "--dotnet-path", fixture.DotnetTool, "--no-build");
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
