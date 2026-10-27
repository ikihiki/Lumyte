using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Lumyte.Text.Parley.Build;

/// <summary>Provides an isolated Python interpreter for the Emscripten toolchain.</summary>
internal static class PythonRuntime
{
    private const long MaximumArchiveBytes = 100 * 1024 * 1024;
    private const long MaximumExtractedBytes = 512 * 1024 * 1024;
    private static readonly HttpClient _client = new() { Timeout = TimeSpan.FromMinutes(10) };

    public static async Task<string> ResolveAsync(string repositoryRoot, string? emsdkTools, string? overridePath = null)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            string explicitPath = Path.GetFullPath(overridePath);
            await VerifyInterpreterAsync(explicitPath).ConfigureAwait(false);
            return explicitPath;
        }

        string? bundled = FindBundledInterpreter(emsdkTools);
        if (bundled is not null)
        {
            await VerifyInterpreterAsync(bundled).ConfigureAwait(false);
            return bundled;
        }

        string runtimeId = RuntimeId();
        using Stream metadata = Assembly.GetExecutingAssembly().GetManifestResourceStream("Lumyte.Text.Parley.Build.PythonRuntimes.json")
            ?? throw new InvalidOperationException("The pinned Python runtime metadata is missing.");
        Dictionary<string, RuntimeSpec> runtimes = JsonSerializer.Deserialize<Dictionary<string, RuntimeSpec>>(metadata)
            ?? throw new InvalidDataException("The pinned Python runtime metadata is invalid.");
        if (!runtimes.TryGetValue(runtimeId, out RuntimeSpec? runtime))
        {
            throw new PlatformNotSupportedException($"A private Python runtime is not configured for {runtimeId}. Supply --python with a Python 3.8 or newer interpreter.");
        }

        string directory = Path.GetFullPath(Path.Combine(repositoryRoot, "artifacts", "text-parley", "tools", "python", runtimeId, runtime.Version));
        string executable = Path.Combine(directory, runtime.Executable);
        Directory.CreateDirectory(Path.GetDirectoryName(directory)!);
        using FileStream cacheLock = await AcquireLockAsync(directory + ".lock").ConfigureAwait(false);
        string marker = Path.Combine(directory, ".verified-sha256");
        if (File.Exists(executable) && File.Exists(marker) && (await File.ReadAllTextAsync(marker).ConfigureAwait(false)).Trim() == runtime.Sha256)
        {
            return executable;
        }

        string temporary = directory + "." + Guid.NewGuid().ToString("N");
        string archive = temporary + ".tar.gz";
        Directory.CreateDirectory(temporary);
        try
        {
            Console.WriteLine($"Preparing private Python {runtime.Version} for Emscripten ({runtimeId}).");
            await DownloadAsync(runtime.Url, archive).ConfigureAwait(false);
            await VerifyArchiveAsync(archive, runtime.Sha256).ConfigureAwait(false);
            await ExtractAsync(archive, temporary).ConfigureAwait(false);
            await VerifyInterpreterAsync(Path.Combine(temporary, runtime.Executable)).ConfigureAwait(false);
            await File.WriteAllTextAsync(Path.Combine(temporary, ".verified-sha256"), runtime.Sha256 + "\n").ConfigureAwait(false);
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }

            Directory.Move(temporary, directory);
            return executable;
        }
        finally
        {
            File.Delete(archive);
            if (Directory.Exists(temporary))
            {
                Directory.Delete(temporary, recursive: true);
            }
        }
    }

    private static string? FindBundledInterpreter(string? emsdkTools)
    {
        if (string.IsNullOrWhiteSpace(emsdkTools))
        {
            return null;
        }

        string[] candidates =
        [
            Path.Combine(emsdkTools, "python", "python.exe"),
            Path.Combine(emsdkTools, "python", "bin", "python3"),
            Path.Combine(emsdkTools, "python", "bin", "python"),
        ];
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string RuntimeId()
    {
        string operatingSystem = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : OperatingSystem.IsLinux() ? "linux" : throw new PlatformNotSupportedException("A private Python runtime is available for Windows, macOS and Linux.");
        string architecture = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => throw new PlatformNotSupportedException("A private Python runtime requires an x64 or Arm64 operating system."),
        };
        return operatingSystem + "-" + architecture;
    }

    private static async Task<FileStream> AcquireLockAsync(string path)
    {
        DateTime deadline = DateTime.UtcNow.AddMinutes(15);
        while (true)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(100).ConfigureAwait(false);
            }
        }
    }

    private static async Task DownloadAsync(string url, string destination)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        CancellationToken cancellationToken = timeout.Token;
        using HttpResponseMessage response = await _client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long length && length > MaximumArchiveBytes)
        {
            throw new InvalidDataException("The Python runtime archive exceeds the download limit.");
        }

        await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var target = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        byte[] buffer = new byte[81920];
        long total = 0;
        int count;
        while ((count = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) != 0)
        {
            total += count;
            if (total > MaximumArchiveBytes)
            {
                throw new InvalidDataException("The Python runtime archive exceeds the download limit.");
            }

            await target.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task VerifyArchiveAsync(string archive, string expected)
    {
        await using FileStream stream = File.OpenRead(archive);
        byte[] digest = await SHA256.HashDataAsync(stream).ConfigureAwait(false);
        if (!Convert.ToHexString(digest).Equals(expected, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The private Python archive does not match its pinned SHA-256 checksum.");
        }
    }

    private static async Task ExtractAsync(string archive, string directory)
    {
        await ValidateArchiveEntriesAsync(archive, directory).ConfigureAwait(false);
        await using FileStream file = File.OpenRead(archive);
        await using var compressed = new GZipStream(file, CompressionMode.Decompress);
        await TarFile.ExtractToDirectoryAsync(compressed, directory, overwriteFiles: false).ConfigureAwait(false);
    }

    private static async Task ValidateArchiveEntriesAsync(string archive, string directory)
    {
        await using FileStream file = File.OpenRead(archive);
        await using var compressed = new GZipStream(file, CompressionMode.Decompress);
        await using var reader = new TarReader(compressed);
        long total = 0;
        int count = 0;
        while (await reader.GetNextEntryAsync().ConfigureAwait(false) is TarEntry entry)
        {
            if (++count > 30000 || (total += entry.Length) > MaximumExtractedBytes)
            {
                throw new InvalidDataException("The Python runtime archive exceeds the extraction limit.");
            }

            if (entry.EntryType is TarEntryType.GlobalExtendedAttributes or TarEntryType.ExtendedAttributes)
            {
                continue;
            }

            if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile or TarEntryType.Directory or TarEntryType.SymbolicLink or TarEntryType.HardLink))
            {
                throw new InvalidDataException("The Python runtime archive contains an unsupported entry type.");
            }

            if (entry.Name.Contains('\\', StringComparison.Ordinal) || (entry.Name != "python" && !entry.Name.StartsWith("python/", StringComparison.Ordinal)))
            {
                throw new InvalidDataException("The Python runtime archive contains an unexpected path.");
            }

            string fullPath = ContainedPath(directory, entry.Name);
            if (entry.EntryType is TarEntryType.SymbolicLink or TarEntryType.HardLink)
            {
                string parent = entry.EntryType == TarEntryType.SymbolicLink ? Path.GetDirectoryName(fullPath)! : directory;
                _ = ContainedPath(directory, Path.Combine(parent, entry.LinkName));
            }
        }
    }

    private static string ContainedPath(string directory, string path)
    {
        string root = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
        string resolved = Path.GetFullPath(Path.Combine(directory, path));
        StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (!resolved.StartsWith(root, comparison))
        {
            throw new InvalidDataException("The Python runtime archive contains a path outside its extraction directory.");
        }

        return resolved;
    }

    private static async Task VerifyInterpreterAsync(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The Python interpreter was not found.", path);
        }

        var start = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("-I");
        start.ArgumentList.Add("--version");
        start.Environment.Remove("PYTHONHOME");
        start.Environment.Remove("PYTHONPATH");
        using Process process = Process.Start(start) ?? throw new InvalidOperationException("The Python interpreter could not be started.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        Task<string> error = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync().ConfigureAwait(false);
        string versionText = ((await output.ConfigureAwait(false)) + (await error.ConfigureAwait(false))).Trim();
        if (process.ExitCode != 0 || !versionText.StartsWith("Python ", StringComparison.Ordinal) || !Version.TryParse(versionText[7..], out Version? version) || version < new Version(3, 8))
        {
            throw new InvalidOperationException($"Emscripten requires Python 3.8 or newer; interpreter probe returned '{versionText}' (exit {process.ExitCode}).");
        }
    }

    private sealed record RuntimeSpec(string Version, string Url, string Sha256, string Executable);
}
