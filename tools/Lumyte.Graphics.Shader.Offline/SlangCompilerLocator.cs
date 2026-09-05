using System.IO.Compression;
using System.Security.Cryptography;

namespace Lumyte.Graphics.Shader.Offline;

public static class SlangCompilerLocator
{
    public static async Task<string> ResolveAsync(string cacheDirectory, CancellationToken cancellationToken = default)
    {
        string? configured = Environment.GetEnvironmentVariable("SLANGC_PATH");
        if (!string.IsNullOrWhiteSpace(configured))
        {
            string fullPath = Path.GetFullPath(configured);
            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException("SLANGC_PATH does not point to a file.", fullPath);
            }

            return fullPath;
        }

        SlangReleaseAsset asset = SlangRelease.GetCurrentAsset();
        string versionDirectory = Path.Combine(Path.GetFullPath(cacheDirectory), SlangRelease.Version);
        string executable = Path.Combine(versionDirectory, "bin", OperatingSystem.IsWindows() ? "slangc.exe" : "slangc");
        if (File.Exists(executable))
        {
            return executable;
        }

        Directory.CreateDirectory(versionDirectory);
        string archive = Path.Combine(versionDirectory, asset.ArchiveName);
        if (!File.Exists(archive))
        {
            using var client = new HttpClient();
            using HttpResponseMessage response = await client.GetAsync(asset.DownloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using FileStream destination = File.Create(archive);
            await source.CopyToAsync(destination, cancellationToken);
        }

        await VerifyAsync(archive, asset.Sha512, cancellationToken);
        ZipFile.ExtractToDirectory(archive, versionDirectory, overwriteFiles: true);
        if (!File.Exists(executable))
        {
            throw new InvalidDataException($"The Slang archive does not contain {Path.GetFileName(executable)}.");
        }

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(executable, File.GetUnixFileMode(executable) | UnixFileMode.UserExecute);
        }

        return executable;
    }

    private static async Task VerifyAsync(string path, string expected, CancellationToken cancellationToken)
    {
        await using FileStream stream = File.OpenRead(path);
        byte[] actual = await SHA512.HashDataAsync(stream, cancellationToken);
        if (!CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(expected)))
        {
            throw new InvalidDataException("The downloaded Slang archive failed SHA-512 verification.");
        }
    }
}
