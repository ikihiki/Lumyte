using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xml.Linq;
using Xunit;

namespace Lumyte.Build.Tests;

/// <summary>Checks category compilation uses the same canonical classification as execution.</summary>
public sealed class ScopedBuildTests
{
    /// <summary>Explicit compilation and automatic Test compilation share exact roots.</summary>
    /// <param name="lane">The requested category.</param>
    /// <returns>The completed orchestration check.</returns>
    [Theory]
    [InlineData("cpu")]
    [InlineData("gpu")]
    [InlineData("integration")]
    [Trait("Execution", "Integration")]
    public async Task CompileAndTestUseMatchingCatalogRootsAsync(string lane)
    {
        if (lane == "cpu" && (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64))
        {
            return;
        }

        using var fixture = new BuildFixture();
        Prepare(fixture);
        BuildFixture.ProcessResult compile = await fixture.RunAsync("Compile", "--build-lane", lane, "--dotnet-path", fixture.DotnetTool);
        Assert.True(compile.ExitCode == 0, compile.Output + compile.Error);
        JsonElement[] records = fixture.Records("dotnet");
        Assert.Equal(new[] { "restore", "build" }, records.Select(record => record.GetProperty("arguments")[0].GetString()));
        string buildFilter = records[1].GetProperty("arguments")[1].GetString()!;
        Assert.Equal(records[0].GetProperty("arguments")[1].GetString(), buildFilter);
        AssertFilter(fixture, buildFilter, lane + ".csproj");
        BuildFixture.ProcessResult test = await fixture.RunAsync("Test", "--lane", lane, "--dotnet-path", fixture.DotnetTool);
        Assert.True(test.ExitCode == 0, test.Output + test.Error);
        records = fixture.Records("dotnet");
        Assert.Equal(new[] { "restore", "build", "restore", "build", "test" }, records.Select(record => record.GetProperty("arguments")[0].GetString()));
        Assert.Equal(buildFilter, records[3].GetProperty("arguments")[1].GetString());
        AssertFilter(fixture, records[4].GetProperty("arguments")[1].GetString()!, lane + ".csproj");
    }

    /// <summary>Unclassified additions fail before restore/build/test, even with compilation enabled.</summary>
    /// <param name="target">The task under validation.</param>
    /// <returns>The completed fail-closed assertion.</returns>
    [Theory]
    [InlineData("Compile")]
    [InlineData("Test")]
    [Trait("Execution", "Integration")]
    public async Task NewProjectGuardRunsBeforeAllToolsAsync(string target)
    {
        using var fixture = new BuildFixture();
        Prepare(fixture);
        File.WriteAllText(Path.Combine(fixture.Root, "build", "TestLanes.json"), "[]");
        BuildFixture.ProcessResult result = await fixture.RunAsync(target, target == "Compile" ? "--build-lane" : "--lane", "integration", "--dotnet-path", fixture.DotnetTool);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Classify", result.Output + result.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(fixture.Log, "dotnet.jsonl")));
    }

    /// <summary>Invalid catalog presence and duplicate identities never launch the SDK.</summary>
    /// <param name="duplicate">Whether to duplicate an entry instead of corrupting its lanes.</param>
    /// <returns>The completed catalog assertion.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Execution", "Integration")]
    public async Task InvalidCatalogFailsBeforeCompilationAsync(bool duplicate)
    {
        using var fixture = new BuildFixture();
        Prepare(fixture);
        string catalog = Path.Combine(fixture.Root, "build", "TestLanes.json");
        string content = File.ReadAllText(catalog);
        File.WriteAllText(catalog, duplicate ? content[..^1] + "," + content[1..] : content.Replace("[\"Cpu\"]", "[]", StringComparison.Ordinal));
        BuildFixture.ProcessResult result = await fixture.RunAsync("Compile", "--build-lane", "integration", "--dotnet-path", fixture.DotnetTool);
        Assert.NotEqual(0, result.ExitCode);
        Assert.False(File.Exists(Path.Combine(fixture.Log, "dotnet.jsonl")));
    }

    /// <summary>No category selector preserves the full host solution, including samples.</summary>
    /// <returns>The completed compatibility check.</returns>
    [Fact]
    [Trait("Execution", "Integration")]
    public async Task DefaultCompilePreservesFullHostSolutionAsync()
    {
        using var fixture = new BuildFixture();
        Prepare(fixture);
        BuildFixture.ProcessResult result = await fixture.RunAsync("Compile", "--host-only", "--dotnet-path", fixture.DotnetTool);
        Assert.True(result.ExitCode == 0, result.Output + result.Error);
        string solution = fixture.Records("dotnet")[1].GetProperty("arguments")[1].GetString()!;
        Assert.EndsWith(".slnx", solution, StringComparison.Ordinal);
        Assert.Equal(4, XDocument.Load(solution).Descendants("Project").Count());
    }

    /// <summary>The pinned SDK builds only selected roots and their evaluated transitive references.</summary>
    /// <returns>The completed real SDK closure check.</returns>
    [Fact]
    [Trait("Execution", "Integration")]
    public async Task RealSdkBuildsTransitiveClosureWithoutUnrelatedProjectsAsync()
    {
        using var fixture = new BuildFixture();
        string[] names = ["Root", "Middle", "Leaf", "Unrelated"];
        foreach (string name in names)
        {
            string directory = Path.Combine(fixture.Root, name);
            Directory.CreateDirectory(directory);
            string? dependency = name switch { "Root" => "Middle", "Middle" => "Leaf", _ => null };
            var project = new XElement("Project", new XAttribute("Sdk", "Microsoft.NET.Sdk"), new XElement("PropertyGroup", new XElement("TargetFramework", "net10.0"), new XElement("Configuration", "Release"), new XElement("ShouldUnsetParentConfigurationAndPlatform", "false"), new XElement("IsTestProject", name == "Root" ? "true" : "false")));
            if (dependency is not null)
            {
                project.Add(new XElement("ItemGroup", new XElement("ProjectReference", new XAttribute("Include", "../" + dependency + "/" + dependency + ".csproj"))));
            }

            File.WriteAllText(Path.Combine(directory, name + ".csproj"), new XDocument(project).ToString());
        }

        // Leaf is intentionally absent from the canonical solution: the SDK,
        // not a source-text graph walker, owns project-reference closure.
        File.WriteAllText(fixture.Solution, new XDocument(new XElement("Solution", new[] { "Root", "Middle", "Unrelated" }.Select(name => new XElement("Project", new XAttribute("Path", name + "/" + name + ".csproj"))))).ToString());
        Directory.CreateDirectory(Path.Combine(fixture.Root, "build"));
        File.WriteAllText(Path.Combine(fixture.Root, "build", "TestLanes.json"), """[{"Project":"Root/Root.csproj","Default":"Cpu","IntegrationClasses":[],"Lanes":["Cpu"]}]""");
        string dotnet = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet"));
        BuildFixture.ProcessResult result = await fixture.RunAsync("Compile", "--build-lane", "cpu", "--dotnet-path", dotnet);
        Assert.True(result.ExitCode == 0, result.Output + result.Error);
        foreach (string name in new[] { "Root", "Middle", "Leaf" })
        {
            Assert.True(File.Exists(Path.Combine(fixture.Root, name, "bin", "Release", "net10.0", name + ".dll")), name + " was not compiled. " + result.Output + result.Error);
        }

        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "Unrelated", "bin")));
        Assert.False(Directory.Exists(Path.Combine(fixture.Root, "Unrelated", "obj")));
    }

    private static void AssertFilter(BuildFixture fixture, string path, string project)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement solution = document.RootElement.GetProperty("solution");
        Assert.Equal(fixture.Solution, Path.GetFullPath(solution.GetProperty("path").GetString()!, Path.GetDirectoryName(path)!));
        Assert.Equal(project, Assert.Single(solution.GetProperty("projects").EnumerateArray()).GetString());
    }

    private static void Prepare(BuildFixture fixture)
    {
        string[] names = ["cpu", "gpu", "integration", "Sample"];
        foreach (string name in names)
        {
            File.WriteAllText(Path.Combine(fixture.Root, name + ".csproj"), "<Project><PropertyGroup><IsTestProject>" + (name == "Sample" ? "false" : "true") + "</IsTestProject></PropertyGroup></Project>");
        }

        File.WriteAllText(fixture.Solution, new XDocument(new XElement("Solution", names.Select(name => new XElement("Project", new XAttribute("Path", name + ".csproj"))))).ToString());
        Directory.CreateDirectory(Path.Combine(fixture.Root, "build"));
        File.WriteAllText(Path.Combine(fixture.Root, "build", "TestLanes.json"), JsonSerializer.Serialize(new[]
        {
            new { Project = "cpu.csproj", Default = "Cpu", IntegrationClasses = Array.Empty<string>(), Lanes = new[] { "Cpu" } },
            new { Project = "gpu.csproj", Default = "Integration", IntegrationClasses = Array.Empty<string>(), Lanes = new[] { "Gpu" } },
            new { Project = "integration.csproj", Default = "Integration", IntegrationClasses = Array.Empty<string>(), Lanes = new[] { "Integration" } },
        }));
    }
}
