using System.Text.Json;
using Xunit;

namespace Lumyte.Build.Tests;

/// <summary>Checks the repository targets through real Fallout execution and controlled dotnet processes.</summary>
public sealed class RepositoryBuildTests
{
    /// <summary>Compilation restores once, preserves path boundaries and uses the requested application configuration.</summary>
    /// <returns>The asynchronous process assertion.</returns>
    [Fact]
    public async Task CompileRestoresThenBuildsTheRequestedProjectAsync()
    {
        using var fixture = new BuildFixture();
        BuildFixture.ProcessResult result = await fixture.RunAsync("Compile", "--dotnet-path", fixture.DotnetTool, "--project", fixture.Project, "--configuration", "Debug");
        AssertSuccess(result);
        JsonElement[] records = fixture.Records("dotnet");
        Assert.Equal(["restore", "build"], Commands(records));
        Assert.All(records, record => Assert.Contains(fixture.Project, Arguments(record)));
        string[] build = Arguments(records[1]);
        Assert.Equal("Debug", Value(build, "--configuration"));
        Assert.Contains("--no-restore", build);
        string assembly = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "runner-path.txt")).Trim();
        Assert.All(records, record => Assert.Equal(assembly, record.GetProperty("environment").GetProperty("LUMYTE_BUILD_ASSEMBLY").GetString()));
    }

    /// <summary>Tests depend on the explicit restore/build targets and suppress their own implicit work.</summary>
    /// <returns>The asynchronous process assertion.</returns>
    [Fact]
    public async Task TestRunsOnlyAfterTheExplicitBuildAsync()
    {
        using var fixture = new BuildFixture();
        BuildFixture.ProcessResult result = await fixture.RunAsync("Test", "--dotnet-path", fixture.DotnetTool, "--project", fixture.Project);
        AssertSuccess(result);
        JsonElement[] records = fixture.Records("dotnet");
        Assert.Equal(["restore", "build", "test"], Commands(records));
        string[] test = Arguments(records[^1]);
        Assert.Contains(fixture.Project, test);
        Assert.Equal("Release", Value(test, "--configuration"));
        Assert.Contains("--no-build", test);
        Assert.Contains("--no-restore", test);
    }

    /// <summary>A failed compile cannot continue into the test process.</summary>
    /// <returns>The asynchronous failure assertion.</returns>
    [Fact]
    public async Task BuildFailureStopsDependentTestsAsync()
    {
        using var fixture = new BuildFixture();
        fixture.Environment["LUMYTE_TEST_DOTNET_FAIL_COMMAND"] = "build";
        fixture.Environment["LUMYTE_TEST_DOTNET_EXIT"] = "23";
        BuildFixture.ProcessResult result = await fixture.RunAsync("Test", "--dotnet-path", fixture.DotnetTool, "--project", fixture.Project);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("23", result.Output + result.Error, StringComparison.Ordinal);
        Assert.Equal(["restore", "build"], Commands(fixture.Records("dotnet")));
    }

    /// <summary>No-build mode uses staged test outputs without restoring or compiling the project.</summary>
    /// <returns>The asynchronous reuse assertion.</returns>
    [Fact]
    public async Task NoBuildRunsOnlyTheTestProcessAsync()
    {
        using var fixture = new BuildFixture();
        BuildFixture.ProcessResult result = await fixture.RunAsync("Test", "--dotnet-path", fixture.DotnetTool, "--project", fixture.Project, "--no-build");
        AssertSuccess(result);
        JsonElement test = Assert.Single(fixture.Records("dotnet"));
        Assert.Equal("test", Arguments(test)[0]);
        Assert.Contains("--no-build", Arguments(test));
        Assert.Contains("--no-restore", Arguments(test));
    }

    /// <summary>No-restore mode still compiles while avoiding a redundant restore target.</summary>
    /// <returns>The asynchronous reuse assertion.</returns>
    [Fact]
    public async Task NoRestoreStillBuildsTheRequestedProjectAsync()
    {
        using var fixture = new BuildFixture();
        BuildFixture.ProcessResult result = await fixture.RunAsync("Compile", "--dotnet-path", fixture.DotnetTool, "--project", fixture.Project, "--no-restore");
        AssertSuccess(result);
        JsonElement build = Assert.Single(fixture.Records("dotnet"));
        Assert.Equal("build", Arguments(build)[0]);
        Assert.Contains("--no-restore", Arguments(build));
    }

    /// <summary>Publish keeps the selected project, runtime and Unicode output as separate arguments.</summary>
    /// <returns>The asynchronous publication assertion.</returns>
    [Fact]
    public async Task PublishForwardsRuntimeAndOutputAsync()
    {
        using var fixture = new BuildFixture();
        BuildFixture.ProcessResult result = await fixture.RunAsync("Publish", "--dotnet-path", fixture.DotnetTool, "--project", fixture.Project, "--runtime", "linux-x64");
        AssertSuccess(result);
        JsonElement publish = Assert.Single(fixture.Records("dotnet"), record => Arguments(record)[0] == "publish");
        string[] arguments = Arguments(publish);
        Assert.Contains(fixture.Project, arguments);
        Assert.Equal("linux-x64", Value(arguments, "--runtime"));
        Assert.Equal(fixture.Output, Value(arguments, "--output"));
        Assert.Contains("-m:1", arguments);
    }

    /// <summary>Pack selects the requested project and reuses the explicit compile target's output.</summary>
    /// <returns>The asynchronous package assertion.</returns>
    [Fact]
    public async Task PackUsesTheRequestedProjectWithoutImplicitBuildAsync()
    {
        using var fixture = new BuildFixture();
        BuildFixture.ProcessResult result = await fixture.RunAsync("Pack", "--dotnet-path", fixture.DotnetTool, "--project", fixture.Project);
        AssertSuccess(result);
        JsonElement pack = Assert.Single(fixture.Records("dotnet"), record => Arguments(record)[0] == "pack");
        string[] arguments = Arguments(pack);
        Assert.Contains(fixture.Project, arguments);
        Assert.Contains("--no-build", arguments);
        Assert.Contains("--no-restore", arguments);
        Assert.Equal(fixture.Output, Value(arguments, "--output"));
    }

    /// <summary>Default packaging discovers packable source projects while excluding opted-out and intermediate projects.</summary>
    /// <returns>The asynchronous project-discovery assertion.</returns>
    [Fact]
    public async Task PackDiscoversOnlyPackableSourceProjectsAsync()
    {
        using var fixture = new BuildFixture();
        string source = Path.Combine(fixture.Root, "src");
        Directory.CreateDirectory(Path.Combine(source, "obj"));
        string packable = Path.Combine(source, "Packable.csproj");
        File.WriteAllText(packable, File.ReadAllText(fixture.Project));
        File.WriteAllText(Path.Combine(source, "Unpackable.csproj"), "<Project><PropertyGroup><IsPackable>false</IsPackable></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(source, "obj", "Intermediate.csproj"), File.ReadAllText(fixture.Project));
        BuildFixture.ProcessResult result = await fixture.RunAsync("Pack", "--dotnet-path", fixture.DotnetTool, "--no-build");
        AssertSuccess(result);
        JsonElement[] records = fixture.Records("dotnet");
        Assert.Equal(2, records.Count(record => Arguments(record)[0] == "msbuild"));
        JsonElement pack = Assert.Single(records, record => Arguments(record)[0] == "pack");
        Assert.Contains(packable, Arguments(pack));
        Assert.DoesNotContain(records, record => Arguments(record).Any(argument => argument.EndsWith("Intermediate.csproj", StringComparison.Ordinal)));
    }

    /// <summary>Explicit package opt-out avoids invoking dotnet pack for the native Parley package.</summary>
    /// <returns>The asynchronous package-selection assertion.</returns>
    [Fact]
    public async Task SkipParleyPackageDoesNotInvokePackAsync()
    {
        using var fixture = new BuildFixture();
        string project = Path.Combine(fixture.Root, "Lumyte.Graphics.Text.Parley.csproj");
        File.WriteAllText(project, File.ReadAllText(fixture.Project));
        BuildFixture.ProcessResult result = await fixture.RunAsync("Pack", "--dotnet-path", fixture.DotnetTool, "--project", project, "--no-build", "--skip-parley-package");
        AssertSuccess(result);
        Assert.False(Directory.Exists(fixture.Log));
    }

    private static string[] Commands(JsonElement[] records) => records.Select(record => Arguments(record)[0]).ToArray();

    private static string[] Arguments(JsonElement record) => record.GetProperty("arguments").EnumerateArray().Select(value => value.GetString()!).ToArray();

    private static string Value(string[] arguments, string option)
    {
        int index = Array.IndexOf(arguments, option);
        Assert.True(index >= 0 && index + 1 < arguments.Length, "Missing argument: " + option);
        return arguments[index + 1];
    }

    private static void AssertSuccess(BuildFixture.ProcessResult result)
        => Assert.True(result.ExitCode == 0, result.Output + "\n" + result.Error);
}
