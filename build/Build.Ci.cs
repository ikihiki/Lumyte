using System.Xml.Linq;
using Fallout.Common;

namespace Lumyte.Build;

internal sealed partial class Build
{
    private Target CiNative => definition => definition
        .DependsOn(Test);

    private Target CiBrowser => definition => definition
        .DependsOn(TestBrowserContracts, TestWasm, TestBrowserAudio);

    private Target CiDistribution => definition => definition
        .DependsOn(Samples, Pack);

    private Target CiQuality => definition => definition
        .DependsOn(CheckFormat, CheckNativeFormat, CheckMarkdown);

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

    private static bool IsHostProject(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        return name switch
        {
            "Lumyte.Platform.Linux" => OperatingSystem.IsLinux(),
            "Lumyte.Platform.Windows" or "Lumyte.Platform.Windows.Sample"
                or "Lumyte.Graphics.Direct3D12" or "Lumyte.Graphics.Direct3D12.Sample"
                or "Lumyte.Graphics.Direct3D12.Tests" => OperatingSystem.IsWindows(),
            _ => true,
        };
    }
}
