using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

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
        string commit = Git(root, "rev-parse", "HEAD");
        string dirty = Git(root, "diff", "--binary", "HEAD");
        string untracked = Git(root, "ls-files", "--others", "--exclude-standard");
        return new
        {
            sourceCommit = commit,
            dirtyDiffSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dirty))),
            dirtyTracked = dirty.Length != 0,
            untrackedPaths = untracked.Split('\n', StringSplitOptions.RemoveEmptyEntries),
            cpu = CpuIdentity(),
            os = RuntimeInformation.OSDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            runtime = RuntimeInformation.FrameworkDescription,
            sources,
            shaders,
            binaries,
            policy = "SHA256 file contents, captured outside timing; includes untracked harness source; bin/obj excluded from source; runtime output binaries hashed separately; shader source/container hashes are not standalone target-code hashes",
        };
    }

    private static string Git(string root, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start git for source provenance.");
        Task<string> error = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException("Source provenance failed: " + error.GetAwaiter().GetResult());
        }

        return output.TrimEnd();
    }

    private static string? CpuIdentity()
    {
        if (OperatingSystem.IsLinux() && File.Exists("/proc/cpuinfo"))
        {
            return File.ReadLines("/proc/cpuinfo").FirstOrDefault(line => line.StartsWith("model name", StringComparison.Ordinal))?.Split(':', 2)[1].Trim();
        }

        return Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER");
    }

    private static IEnumerable<string> Files(string path) => Directory.Exists(path) ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories) : [];

    private static string Hash(string path)
    {
        using FileStream file = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(file));
    }
}
