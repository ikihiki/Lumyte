using System.Xml.Linq;

namespace Lumyte.Build;

internal sealed partial class Build
{
    private async Task PackAsync()
    {
        string output = Path.GetFullPath(Output ?? Path.Combine(ArtifactRoot, "nuget"));
        Directory.CreateDirectory(output);
        if (Project is string project)
        {
            if (SkipParleyPackage && Path.GetFileName(project) == "Lumyte.Graphics.Text.Parley.csproj")
            {
                return;
            }

            var arguments = new List<string> { "pack", project, "--configuration", Configuration, "--no-build", "--no-restore", "--output", output };
            if (Runtime is not null)
            {
                arguments.Add("-p:RuntimeIdentifier=" + Runtime);
            }

            await DotNetAsync(arguments.ToArray()).ConfigureAwait(false);
            return;
        }

        string[] projects = Directory.EnumerateFiles(Path.Combine(RootDirectory, "src"), "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Where(path => !HostOnly || IsHostProject(path))
            .Where(path => !SkipParleyPackage || Path.GetFileName(path) != "Lumyte.Graphics.Text.Parley.csproj")
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (projects.Length == 0)
        {
            return;
        }

        // Keep project evaluation and IsPackable conditions in MSBuild. One
        // process visits the projects sequentially without restoring or building.
        var document = new XDocument(new XElement("Project",
            new XElement("ItemGroup", projects.Select(path => new XElement("PackageProject", new XAttribute("Include", path)))),
            new XElement("Target", new XAttribute("Name", "Pack"),
                new XElement("MSBuild", new XAttribute("Projects", "@(PackageProject)"), new XAttribute("Targets", "Pack"),
                    new XAttribute("BuildInParallel", "false"), new XAttribute("StopOnFirstFailure", "true")))));
        string traversal = Path.Combine(ArtifactRoot, "ci", "Pack.proj");
        Directory.CreateDirectory(Path.GetDirectoryName(traversal)!);
        document.Save(traversal);
        var batch = new List<string>
        {
            "msbuild", traversal, "-nologo", "-target:Pack", "-p:Configuration=" + Configuration,
            "-p:NoBuild=true", "-p:PackageOutputPath=" + output,
        };
        if (Runtime is not null)
        {
            batch.Add("-p:RuntimeIdentifier=" + Runtime);
        }

        await DotNetAsync(batch.ToArray()).ConfigureAwait(false);
    }
}
