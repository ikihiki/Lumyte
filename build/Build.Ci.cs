using System.Formats.Tar;
using System.Runtime.InteropServices;
using System.Text.Json;
using Fallout.Common;

namespace Lumyte.Build;

internal sealed partial class Build
{
    private Target ArchiveCiBuild => definition => definition
        .Executes(ArchiveCiBuildOutputs);

    private Target VerifyCiBuild => definition => definition
        .Executes(VerifyCiBuildOutputs);

    private void ArchiveCiBuildOutputs()
    {
        RequireCiBuildOutputs();
        string directory = Path.Combine(ArtifactRoot, "ci");
        Directory.CreateDirectory(directory);
        string manifestPath = Path.Combine(directory, "build-manifest.json");
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(CurrentCiBuildManifest()) + Environment.NewLine);

        using FileStream archive = File.Create(Path.Combine(directory, "build.tar"));
        using var writer = new TarWriter(archive, TarEntryFormat.Pax, leaveOpen: false);
        WriteCiArchiveEntry(writer, manifestPath);
        foreach (string root in new[] { "build", "src", "tests", "samples", "benchmarks" })
        {
            foreach (string output in FindCiOutputDirectories(Path.Combine(RootDirectory, root)))
            {
                WriteCiArchiveDirectory(writer, output);
            }
        }

        // Cargo intermediates belong in the build cache; only staged native assets
        // and notices are needed by consumers of this run's compiled outputs.
        string native = Path.Combine(ArtifactRoot, "text-parley", "native");
        foreach (string file in Directory.EnumerateFiles(native).Order(StringComparer.Ordinal))
        {
            WriteCiArchiveEntry(writer, file);
        }

        WriteCiArchiveDirectory(writer, Path.Combine(ArtifactRoot, "text-parley", "package"));
        string direct3D = Path.Combine(ArtifactRoot, "direct3d12");
        if (Directory.Exists(direct3D))
        {
            foreach (string file in Directory.EnumerateFiles(direct3D, "lumyte_graphics_d3d12.dll", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                WriteCiArchiveEntry(writer, file);
            }
        }
    }

    private void VerifyCiBuildOutputs()
    {
        string manifestPath = Path.Combine(ArtifactRoot, "ci", "build-manifest.json");
        CiBuildManifest manifest = JsonSerializer.Deserialize<CiBuildManifest>(File.ReadAllText(manifestPath))
            ?? throw new InvalidOperationException("The CI build artifact manifest is invalid.");
        CiBuildManifest current = CurrentCiBuildManifest();
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (manifest.Version != current.Version || manifest.RuntimeIdentifier != current.RuntimeIdentifier ||
            manifest.Configuration != current.Configuration || !string.Equals(manifest.RepositoryRoot, current.RepositoryRoot, comparison))
        {
            throw new InvalidOperationException("The CI build artifact requires the producer's checkout path, operating system, architecture and configuration. Use the same runner label and checkout location.");
        }

        RequireCiBuildOutputs();
    }

    private CiBuildManifest CurrentCiBuildManifest()
        => new(1, Path.TrimEndingDirectorySeparator(Path.GetFullPath(RootDirectory)), RuntimeInformation.RuntimeIdentifier, Configuration);

    private void RequireCiBuildOutputs()
    {
        string nativeLibrary = OperatingSystem.IsWindows() ? "lumyte_text_parley.dll"
            : OperatingSystem.IsMacOS() ? "liblumyte_text_parley.dylib" : "liblumyte_text_parley.so";
        foreach (string path in new[]
        {
            "build/bin/Release/net10.0/_build.dll",
            $"src/Graphics/Lumyte.Graphics.Text.Parley/bin/{Configuration}/net10.0/Lumyte.Graphics.Text.Parley.dll",
            $"tests/Core/Lumyte.Signals.Tests/bin/{Configuration}/net10.0/Lumyte.Signals.Tests.dll",
            $"artifacts/text-parley/native/{nativeLibrary}",
            "artifacts/text-parley/native/RUST-STANDARD-LIBRARY-NOTICES.html",
        })
        {
            if (!File.Exists(Path.Combine(RootDirectory, path)))
            {
                throw new FileNotFoundException("The CI build outputs are incomplete. Compile the solution before archiving it.", path);
            }
        }

        if (!Directory.Exists(Path.Combine(ArtifactRoot, "text-parley", "package")))
        {
            throw new DirectoryNotFoundException("The staged Parley package assets are missing from the CI build outputs.");
        }
    }

    private static IEnumerable<string> FindCiOutputDirectories(string root)
    {
        foreach (string directory in Directory.EnumerateDirectories(root).Order(StringComparer.Ordinal))
        {
            if (Path.GetFileName(directory) is "bin" or "obj")
            {
                yield return directory;
            }
            else if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0)
            {
                foreach (string output in FindCiOutputDirectories(directory))
                {
                    yield return output;
                }
            }
        }
    }

    private void WriteCiArchiveDirectory(TarWriter writer, string directory)
    {
        WriteCiArchiveEntry(writer, directory);
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
        {
            return;
        }

        foreach (string entry in Directory.EnumerateFileSystemEntries(directory).Order(StringComparer.Ordinal))
        {
            FileAttributes attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.Directory) != 0 && (attributes & FileAttributes.ReparsePoint) == 0)
            {
                WriteCiArchiveDirectory(writer, entry);
            }
            else
            {
                WriteCiArchiveEntry(writer, entry);
            }
        }
    }

    private void WriteCiArchiveEntry(TarWriter writer, string path)
        => writer.WriteEntry(path, Path.GetRelativePath(RootDirectory, path).Replace('\\', '/'));

    private sealed record CiBuildManifest(int Version, string RepositoryRoot, string RuntimeIdentifier, string Configuration);
}
