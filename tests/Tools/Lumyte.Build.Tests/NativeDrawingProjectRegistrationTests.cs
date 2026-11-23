using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Xml.Linq;
using Xunit;

namespace Lumyte.Build.Tests;

/// <summary>Guards solution discovery and distinct outputs for standalone drawing native suites.</summary>
public sealed class NativeDrawingProjectRegistrationTests
{
    private static readonly string[] _projects =
    [
        "tests/Graphics/Lumyte.Graphics.Drawing2D.Tests/GenericGpu/GenericGpu.NativeTests.csproj",
        "tests/Graphics/Lumyte.Graphics.Drawing2D.Tests/GenericGraphNative/GenericGraph.NativeTests.csproj",
    ];

    /// <summary>Both native suites are canonical solution tests with unique assembly and project identities.</summary>
    [Fact]
    public void CanonicalSolutionRegistersUniqueNativeDrawingTests()
    {
        string root = RepositoryRoot();
        var solution = XDocument.Load(Path.Combine(root, "Lumyte.slnx"));
        string[] registered = solution.Descendants("Project").Select(p => (string)p.Attribute("Path")!).ToArray();
        var identities = new HashSet<string>(StringComparer.Ordinal) { "Lumyte.Graphics.Drawing2D.Tests" };
        var projectNames = new HashSet<string>(StringComparer.Ordinal) { "Lumyte.Graphics.Drawing2D.Tests" };
        var drawing = XDocument.Load(Path.Combine(root, "src/Graphics/Lumyte.Graphics.Drawing2D/Lumyte.Graphics.Drawing2D.csproj"));
        string[] friends = drawing.Descendants("InternalsVisibleTo").Select(p => (string)p.Attribute("Include")!).ToArray();
        foreach (string project in _projects)
        {
            Assert.Contains(project, registered);
            var document = XDocument.Load(Path.Combine(root, project));
            Assert.Equal("true", document.Descendants("IsTestProject").Single().Value);
            string assembly = document.Descendants("AssemblyName").Single().Value;
            Assert.True(identities.Add(assembly), $"Colliding native assembly identity: {assembly}");
            Assert.True(projectNames.Add(Path.GetFileNameWithoutExtension(project)));
            Assert.Contains(assembly, friends);
        }
    }

    /// <summary>The GPU lane selects both native projects through the canonical solution and validates discovery evidence.</summary>
    /// <returns>The asynchronous build orchestration assertion.</returns>
    [Fact]
    [Trait("Execution", "Integration")]
    public async Task GpuLaneDiscoversBothDrawingNativeProjectsAsync()
    {
        using var fixture = new BuildFixture();
        new XDocument(new XElement("Solution", _projects.Select(path => new XElement("Project", new XAttribute("Path", path))))).Save(fixture.Solution);
        foreach (string project in _projects)
        {
            string path = Path.GetFullPath(project, fixture.Root);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "<Project><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>");
        }

        Directory.CreateDirectory(Path.Combine(fixture.Root, "build"));
        File.WriteAllText(Path.Combine(fixture.Root, "build", "TestLanes.json"), JsonSerializer.Serialize(_projects.Select(project => new
        {
            Project = project,
            Default = "Cpu",
            IntegrationClasses = Array.Empty<string>(),
            Lanes = new[] { "Gpu" },
        })));
        BuildFixture.ProcessResult result = await fixture.RunAsync("Test", "--lane", "gpu", "--dotnet-path", fixture.DotnetTool, "--host-only", "--no-build", "--no-restore");
        Assert.True(result.ExitCode == 0, result.Output + result.Error);
        JsonElement call = Assert.Single(fixture.Records("dotnet"));
        string[] args = call.GetProperty("arguments").EnumerateArray().Select(value => value.GetString()!).ToArray();
        Assert.Equal("test", args[0]);
        Assert.Contains("--no-build", args);
        Assert.Contains("--no-restore", args);
        Assert.EndsWith(".slnf", args[1], StringComparison.Ordinal);
        Assert.DoesNotContain("--filter", args);
        using var selected = JsonDocument.Parse(File.ReadAllText(args[1]));
        JsonElement solution = selected.RootElement.GetProperty("solution");
        Assert.Equal(fixture.Solution, Path.GetFullPath(solution.GetProperty("path").GetString()!, Path.GetDirectoryName(args[1])!));
        string[] selectedPaths = solution.GetProperty("projects").EnumerateArray().Select(path => Path.GetFullPath(path.GetString()!, fixture.Root)).ToArray();
        Assert.Equal(_projects.Length, selectedPaths.Length);
        string targetsFile = Assert.Single(args, argument => argument.StartsWith("-p:LumyteTestLaneTargets=", StringComparison.Ordinal))["-p:LumyteTestLaneTargets=".Length..];
        var targets = XDocument.Load(targetsFile);
        XElement[] groups = targets.Root!.Elements("PropertyGroup").Where(group => group.Element("LumyteLaneSelected")?.Value == "true").ToArray();
        Assert.Equal(_projects.Length, groups.Length);
        Assert.All(groups, group => Assert.Equal("Execution=Gpu", group.Element("VSTestTestCaseFilter")!.Value));
        string inventory = call.GetProperty("environment").GetProperty("LUMYTE_LANE_INVENTORY").GetString()!;
        foreach (string project in _projects)
        {
            string path = Path.GetFullPath(project, fixture.Root);
            Assert.Contains(path, selectedPaths);
            using var evidence = JsonDocument.Parse(File.ReadAllText(Path.Combine(inventory, Path.GetFileName(project) + ".json")));
            Assert.Equal(path, evidence.RootElement.GetProperty("Project").GetString());
            Assert.Equal(JsonValueKind.Null, evidence.RootElement.GetProperty("Error").ValueKind);
        }
    }

    private static string RepositoryRoot([CallerFilePath] string source = "")
        => Path.GetFullPath("../../..", Path.GetDirectoryName(source)!);
}
