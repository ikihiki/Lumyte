using System.IO.Enumeration;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

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

    // Category selection deliberately knows nothing about source paths or type naming.
    internal static JsonObject ParseCatalog(string json)
    {
        JsonObject catalog = JsonNode.Parse(json) as JsonObject
            ?? throw new ArgumentException("Benchmark catalog must be an object.", nameof(json));
        if (catalog["SchemaVersion"] is not JsonValue schema || !schema.TryGetValue<int>(out int version) || version != 1
            || catalog["Cases"] is not JsonArray cases)
        {
            throw new ArgumentException("Benchmark catalog requires SchemaVersion 1 and a Cases array.", nameof(json));
        }

        string[] modes = ReadStrings(catalog["Modes"], "Modes");
        if (modes.Length == 0 || modes.Any(mode => mode is not ("verify" or "screen" or "compare" or "diagnose")))
        {
            throw new ArgumentException("Benchmark catalog Modes must contain supported modes.", nameof(json));
        }

        foreach (JsonNode? item in cases)
        {
            if (item is not JsonObject benchmark)
            {
                throw new ArgumentException("Benchmark catalog Cases must contain objects.", nameof(json));
            }

            ReadStrings(benchmark["Categories"], "Cases.Categories");
            string[] caseModes = ReadStrings(benchmark["Modes"], "Cases.Modes");
            if (caseModes.Length == 0 || caseModes.Any(mode => !modes.Contains(mode, StringComparer.Ordinal)))
            {
                throw new ArgumentException("Benchmark case Modes must be nonempty and declared by the catalog.", nameof(json));
            }
        }

        return catalog;
    }

    internal static JsonArray SelectCases(JsonObject catalog, string mode, string category)
    {
        NormalizeMode(mode);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        return new JsonArray(catalog["Cases"]!.AsArray()
            .Where(benchmark => ReadStrings(benchmark!["Categories"], "Cases.Categories").Contains(category, StringComparer.Ordinal)
                && ReadStrings(benchmark["Modes"], "Cases.Modes").Contains(mode, StringComparer.Ordinal))
            .Select(benchmark => benchmark!.DeepClone()).ToArray());
    }

    internal static void RequireSelectedCases(IEnumerable<JsonArray> selections, string category, string mode)
    {
        if (!selections.Any(cases => cases.Count > 0))
        {
            throw new ArgumentException($"No eligible Core benchmark cases match category '{category}' in mode '{mode}'.", nameof(category));
        }
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

    private static string[] ReadStrings(JsonNode? node, string field)
    {
        if (node is not JsonArray array)
        {
            throw new ArgumentException($"Benchmark catalog {field} must be a string array.");
        }

        return array.Select(item => item is JsonValue value && value.TryGetValue<string>(out string? text) && !string.IsNullOrWhiteSpace(text)
            ? text : throw new ArgumentException($"Benchmark catalog {field} must contain nonempty strings.")).ToArray();
    }
}
