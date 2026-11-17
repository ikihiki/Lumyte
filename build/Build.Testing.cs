using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xml.Linq;
using Fallout.Common;

namespace Lumyte.Build;

internal sealed partial class Build
{
    private Target Test => definition => definition
        .DependsOn(Compile)
        .Executes(async () =>
        {
            await DotNetAsync("test", Project ?? BuildSolution, "--configuration", Configuration, "--no-build", "--no-restore", "-m:1", "--logger", "trx", "--results-directory", Path.Combine(ArtifactRoot, "test-results")).ConfigureAwait(false);
        });

    private Target TestCpu => definition => definition
        .DependsOn(Compile)
        .Executes(() => RunTestLaneAsync("Cpu"));

    private Target TestIntegration => definition => definition
        .DependsOn(Compile)
        .Executes(() => RunTestLaneAsync("Integration"));

    private Target TestGpu => definition => definition
        .DependsOn(Compile)
        .Executes(() => RunTestLaneAsync("Gpu"));

    private async Task RunTestLaneAsync(string lane)
    {
        if (lane == "Cpu" && (!OperatingSystem.IsLinux() || RuntimeInformation.ProcessArchitecture != Architecture.X64))
        {
            throw new PlatformNotSupportedException("The CPU suite runs only on Linux x64. Use TestIntegration/TestGpu on other hosts.");
        }

        TestLane[] catalog = JsonSerializer.Deserialize<TestLane[]>(await File.ReadAllTextAsync(Path.Combine(RepositoryDirectory, "build", "TestLanes.json")).ConfigureAwait(false))!;
        string solution = Path.GetFullPath(Solution, RepositoryDirectory);
        string[] projects = XDocument.Load(solution).Descendants("Project")
            .Select(element => Path.GetFullPath(element.Attribute("Path")!.Value, Path.GetDirectoryName(solution)!))
            .Where(path => File.Exists(path) && XDocument.Load(path).Descendants("IsTestProject").Any(element => element.Value == "true"))
            .ToArray();
        foreach (string path in projects)
        {
            if (!catalog.Any(entry => Path.GetFullPath(entry.Project, RepositoryDirectory) == path))
            {
                throw new InvalidOperationException($"Classify the new test project in build/TestLanes.json: {path}");
            }
        }

        string? selected = Project is null ? null : Path.GetFullPath(Project, RepositoryDirectory).TrimEnd(Path.DirectorySeparatorChar);
        if (selected is not null && !catalog.Any(entry =>
            Path.GetFullPath(entry.Project, RepositoryDirectory) == selected ||
            Path.GetDirectoryName(Path.GetFullPath(entry.Project, RepositoryDirectory)) == selected))
        {
            throw new ArgumentException($"The selected project is not a classified test project: {Project}");
        }

        if (catalog.Select(entry => entry.Project).Distinct(StringComparer.Ordinal).Count() != catalog.Length || catalog.Any(entry => entry.Default is not ("Cpu" or "Integration")))
        {
            throw new InvalidDataException("TestLanes.json contains duplicate projects or an invalid default lane.");
        }

        var environment = new Dictionary<string, string?>();
        if (lane == "Cpu")
        {
            foreach (string key in Environment.GetEnvironmentVariables().Keys.Cast<string>().Where(key => key.StartsWith("LUMYTE_", StringComparison.Ordinal) && (key.Contains("TEST", StringComparison.Ordinal) || key.Contains("BENCHMARK", StringComparison.Ordinal) || key.Contains("GPU", StringComparison.Ordinal))))
            {
                environment[key] = null;
            }
        }

        foreach (TestLane entry in catalog)
        {
            string path = Path.GetFullPath(entry.Project, RepositoryDirectory);
            if (!projects.Contains(path) || !IsHostProject(path) || (selected is not null && selected != path && selected != Path.GetDirectoryName(path)))
            {
                continue;
            }

            string? filter = entry.Filter(lane);
            if (filter is null)
            {
                continue;
            }

            await DotNetWithEnvironmentAsync(environment, "test", path, "--configuration", Configuration, "--no-build", "--no-restore", "-m:1",
                "--filter", filter, "--logger", $"trx;LogFileName={Path.GetFileNameWithoutExtension(path)}.trx", "--results-directory", Path.Combine(ArtifactRoot, "test-results", lane.ToLowerInvariant())).ConfigureAwait(false);
        }
    }

    private sealed record TestLane(string Project, string Default, string[] IntegrationClasses)
    {
        internal string? Filter(string lane)
        {
            if (lane == "Gpu")
            {
                return "Execution=Gpu";
            }

            if (Default == "Integration")
            {
                return lane == "Integration" ? "Execution!=Gpu" : null;
            }

            string[] names = IntegrationClasses.Select(name => $"FullyQualifiedName~.{name}.").ToArray();
            if (lane == "Integration")
            {
                return names.Length == 0 ? "Execution!=Gpu&Execution=Integration" : $"Execution!=Gpu&(Execution=Integration|{string.Join('|', names)})";
            }

            return "Execution!=Gpu&Execution!=Integration" + string.Concat(IntegrationClasses.Select(name => $"&FullyQualifiedName!~.{name}."));
        }
    }
}
