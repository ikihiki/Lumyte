using System.Security.Cryptography;

namespace Lumyte.Graphics.Gpu.Benchmarks;

internal static class Provenance
{
    internal static object Capture()
    {
        string root = Directory.GetCurrentDirectory();
        while (!Directory.Exists(Path.Combine(root, ".git")) && !File.Exists(Path.Combine(root, ".git")))
        {
            root = Directory.GetParent(root)?.FullName ?? throw new InvalidOperationException("Run within the repository checkout for source provenance.");
        }

        string[] sourceRoots = ["benchmarks/Graphics", "tests/Graphics/Lumyte.Graphics.Benchmarks.Tests", "tools/benchmarks/drawing2d-incremental", "tools/benchmarks/graphics-scenarios"];
        var sources = sourceRoots.SelectMany(path => Files(Path.Combine(root, path)))
            .Where(path => !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Where(path => Path.GetExtension(path) is ".cs" or ".csproj" or ".props" or ".targets" or ".json" or ".py" or ".sh")
            .Order(StringComparer.Ordinal).ToDictionary(path => Path.GetRelativePath(root, path), Hash);
        var shaders = Files(Path.Combine(root, "src/Graphics")).Where(path => path.EndsWith(".slang", StringComparison.Ordinal) && !path.Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            .Order(StringComparer.Ordinal).ToDictionary(path => Path.GetRelativePath(root, path), Hash);
        var binaries = Files(AppContext.BaseDirectory).Where(path => Path.GetExtension(path) is ".dll" or ".so" or ".spv")
            .Order(StringComparer.Ordinal).ToDictionary(path => Path.GetRelativePath(AppContext.BaseDirectory, path), Hash);
        return new { sources, shaders, binaries, policy = "SHA256 file contents, captured outside timing; includes untracked harness source; bin/obj excluded from source; runtime output binaries hashed separately" };
    }

    private static IEnumerable<string> Files(string path) => Directory.Exists(path) ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories) : [];

    private static string Hash(string path)
    {
        using FileStream file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file));
    }
}
