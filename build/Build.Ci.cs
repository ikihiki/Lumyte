using System.Xml.Linq;
using Fallout.Common;

namespace Lumyte.Build;

internal sealed partial class Build
{
    [Parameter("Distribution output: all (samples and packages), packages, window-sample, or diagnostic-gallery.")]
    public string DistributionOutput { get; set; } = "all";

    [Parameter("Restore metadata before packaging downloaded binaries without rebuilding them.")]
    public bool RestoreMetadata { get; set; }

    private Target Distribution => definition => definition.Executes(async () =>
    {
        if (DistributionOutput is not ("all" or "packages" or "window-sample" or "diagnostic-gallery"))
        {
            throw new ArgumentException("Invalid --distribution-output.");
        }

        if (RestoreMetadata && (DistributionOutput != "packages" || !NoBuild || NoRestore))
        {
            throw new ArgumentException("--restore-metadata requires --distribution-output packages --no-build without --no-restore.");
        }

        if (DistributionOutput == "diagnostic-gallery")
        {
            await GenerateDiagnosticGalleryAsync().ConfigureAwait(false);
            return;
        }

        if (DistributionOutput == "window-sample" && !OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The window sample requires Windows.");
        }

        await CompileAsync().ConfigureAwait(false);
        if (DistributionOutput == "window-sample")
        {
            await PublishWindowSampleAsync().ConfigureAwait(false);
            return;
        }

        // Packaging downloaded binaries still needs metadata, but must not rebuild them.
        if (RestoreMetadata)
        {
            await RestoreAsync().ConfigureAwait(false);
        }

        if (DistributionOutput == "all")
        {
            await RunSamplesAsync().ConfigureAwait(false);
        }

        await PackAsync().ConfigureAwait(false);
    });

    // Derive the host solution from the canonical list so new shared projects
    // automatically participate without maintaining multiple project inventories.
    private string BuildSolution
    {
        get
        {
            if (!HostOnly)
            {
                return Solution;
            }

            string source = Path.GetFullPath(Solution, RootDirectory);
            var document = XDocument.Load(source);
            foreach (XElement project in document.Descendants("Project").Where(element => element.Attribute("Path") is not null).ToArray())
            {
                string path = Path.GetFullPath(project.Attribute("Path")!.Value, Path.GetDirectoryName(source)!);
                if (!IsHostProject(path))
                {
                    project.Remove();
                    continue;
                }

                project.SetAttributeValue("Path", path);
            }

            string destination = Path.Combine(ArtifactRoot, "ci", OperatingSystem.IsWindows() ? "Windows.slnx" : "Linux.slnx");
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            string content = document.ToString();
            if (!File.Exists(destination) || File.ReadAllText(destination) != content)
            {
                File.WriteAllText(destination, content);
            }

            return destination;
        }
    }

    private void ValidateBuildLane()
    {
        if (BuildLane is not null && BuildLane is not ("cpu" or "gpu" or "integration"))
        {
            throw new ArgumentException("Invalid --build-lane. Expected cpu, gpu, or integration.");
        }
    }

    private string CreateLaneBuildSolution(string lane)
    {
        (string solution, TestLaneSelection[] projects) = SelectTestLane(lane switch { "cpu" => "Cpu", "integration" => "Integration", "gpu" => "Gpu", _ => throw new ArgumentException("Invalid build lane.") });
        string directory = Path.Combine(ArtifactRoot, "ci");
        Directory.CreateDirectory(directory);
        string destination = Path.Combine(directory, lane + ".slnf");
        // The SDK follows ProjectReference transitively, including helpers outside
        // the solution. Only classified roots come from the authoritative catalog;
        // no second dependency inventory or source-text reference evaluator is used.
        string content = System.Text.Json.JsonSerializer.Serialize(new
        {
            solution = new
            {
                path = Path.GetRelativePath(directory, solution),
                projects = projects.Select(project => Path.GetRelativePath(Path.GetDirectoryName(solution)!, project.Path)).ToArray(),
            },
        });
        if (!File.Exists(destination) || File.ReadAllText(destination) != content)
        {
            File.WriteAllText(destination, content);
        }

        return destination;
    }

    private static bool IsHostProject(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        return name switch
        {
            "Lumyte.Platform.Linux" => OperatingSystem.IsLinux(),
            "Lumyte.Platform.Windows" or "Lumyte.Platform.Windows.Sample"
                or "Lumyte.Graphics.Direct3D12" or "Lumyte.Graphics.Direct3D12.Sample"
                or "Lumyte.Graphics.Direct3D12.Tests" or "Lumyte.Platform.Windows.TextInput.Tests" => OperatingSystem.IsWindows(),
            _ => true,
        };
    }
}
