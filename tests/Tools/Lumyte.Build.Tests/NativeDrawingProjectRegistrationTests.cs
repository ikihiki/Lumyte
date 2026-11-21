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

    /// <summary>CiNative keeps both projects when deriving the host solution and invokes the maintained solution runner.</summary>
    /// <returns>The asynchronous build orchestration assertion.</returns>
    [Fact]
    public async Task CiNativeDiscoversBothDrawingNativeProjectsAsync()
    {
        using var fixture = new BuildFixture();
        new XDocument(new XElement("Solution", _projects.Select(path => new XElement("Project", new XAttribute("Path", path))))).Save(fixture.Solution);
        BuildFixture.ProcessResult result = await fixture.RunAsync("CiNative", "--dotnet-path", fixture.DotnetTool, "--host-only", "--no-build", "--no-restore");
        Assert.True(result.ExitCode == 0, result.Output + result.Error);
        JsonElement call = Assert.Single(fixture.Records("dotnet"));
        string[] args = call.GetProperty("arguments").EnumerateArray().Select(value => value.GetString()!).ToArray();
        Assert.Equal("test", args[0]);
        Assert.Contains("--no-build", args);
        Assert.Contains("--no-restore", args);
        var selected = XDocument.Load(args[1]);
        string[] selectedPaths = selected.Descendants("Project").Select(p => (string)p.Attribute("Path")!).ToArray();
        foreach (string project in _projects)
        {
            Assert.Contains(Path.GetFullPath(project, fixture.Root), selectedPaths);
        }
    }

    private static string RepositoryRoot([CallerFilePath] string source = "")
        => Path.GetFullPath("../../..", Path.GetDirectoryName(source)!);
}
