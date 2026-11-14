using System.IO.Enumeration;
using System.Security.Cryptography;
using System.Text;

namespace Lumyte.Build;

internal static class CoreBenchmarkCatalog
{
    internal static string NormalizeMode(string mode) => mode is "verify" or "screen" or "compare" or "diagnose"
        ? mode : throw new ArgumentException("Benchmark mode must be verify, screen, compare, or diagnose.", nameof(mode));

    internal static string[] SelectProjects(string repository, string pattern)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);
        string root = Path.Combine(repository, "benchmarks", "Core");
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(root);
        }

        string[] projects = Directory.GetDirectories(root, "*.Benchmark", SearchOption.TopDirectoryOnly)
            .Select(directory => Path.Combine(directory, Path.GetFileName(directory) + ".csproj"))
            .Where(File.Exists)
            .Where(project => FileSystemName.MatchesSimpleExpression(pattern, Path.GetFileNameWithoutExtension(project), ignoreCase: false)
                || FileSystemName.MatchesSimpleExpression(pattern, Path.GetFileNameWithoutExtension(project).AsSpan()[..^".Benchmark".Length], ignoreCase: false))
            .Order(StringComparer.Ordinal).ToArray();
        return projects.Length > 0 ? projects : throw new ArgumentException("No Core benchmark projects match: " + pattern, nameof(pattern));
    }

    internal static string[] Arguments(string binary, string mode, string filter, string? category, string output, bool list)
    {
        NormalizeMode(mode);
        ArgumentException.ThrowIfNullOrWhiteSpace(filter);
        if (list)
        {
            return [binary, "--catalog"];
        }

        if (mode == "verify")
        {
            return [binary, "--mode", "verify"];
        }

        var args = new List<string> { binary, "--mode", mode, "--filter", filter, "--artifacts", output };
        if (!string.IsNullOrWhiteSpace(category))
        {
            args.AddRange(["--anyCategories", category]);
        }

        return args.ToArray();
    }

    internal static string OutputHash(string directory)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        IEnumerable<string> files = Directory.GetFiles(directory, "*", SearchOption.TopDirectoryOnly)
            .Where(file => file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase));
        string runtimes = Path.Combine(directory, "runtimes");
        if (Directory.Exists(runtimes))
        {
            files = files.Concat(Directory.GetFiles(runtimes, "*", SearchOption.AllDirectories));
        }

        foreach (string file in files.Order(StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(Path.GetRelativePath(directory, file).Replace('\\', '/') + "\0"));
            hash.AppendData(SHA256.HashData(File.ReadAllBytes(file)));
        }

        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }
}
