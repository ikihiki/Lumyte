using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace Lumyte.Text.Parley.Build;

/// <summary>Resolves Rust without installing tools or changing profiles outside explicitly configured roots.</summary>
internal sealed record RustToolchain(string CargoPath, string RustcPath, string RustupPath, string Toolchain, IReadOnlyDictionary<string, string> EnvironmentOverrides)
{
    private static readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static async Task<RustToolchain> ResolveAsync(string repositoryRoot, string toolchain, bool wasm)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolchain);
        if (toolchain.Length > 128 || !char.IsAsciiLetterOrDigit(toolchain[0]) || toolchain.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '.' and not '-' and not '_'))
        {
            throw new ArgumentException("The Rust toolchain must be a named rustup toolchain.", nameof(toolchain));
        }

        toolchain = QualifyToolchain(toolchain);
        string root = Path.GetFullPath(repositoryRoot);
        string tools = Path.Combine(root, "artifacts", "text-parley", "tools");
        string? configuredCargo = Environment.GetEnvironmentVariable("CARGO_HOME");
        string? configuredRustup = Environment.GetEnvironmentVariable("RUSTUP_HOME");
        string? existing = FindRustup(configuredCargo);
        if (existing is not null)
        {
            string? sysroot = await ProbeAsync(existing, toolchain, root, null).ConfigureAwait(false);
            if (sysroot is not null && (!wasm || HasSources(sysroot)))
            {
                return await CreateAsync(existing, toolchain, tools, root, new Dictionary<string, string>()).ConfigureAwait(false);
            }
        }

        // A partially configured environment remains explicit. Otherwise install
        // missing components only beneath the repository's ignored build cache.
        string rustupHome = string.IsNullOrWhiteSpace(configuredRustup) ? Path.Combine(tools, "rustup") : Path.GetFullPath(configuredRustup);
        string cargoHome = string.IsNullOrWhiteSpace(configuredCargo) ? Path.Combine(tools, "cargo") : Path.GetFullPath(configuredCargo);
        var environment = new Dictionary<string, string>
        {
            ["RUSTUP_HOME"] = rustupHome,
            ["CARGO_HOME"] = cargoHome,
        };
        string privateRustup = Path.Combine(tools, "cargo", "bin", Executable("rustup"));
        string rustup = File.Exists(privateRustup) ? privateRustup : existing ?? privateRustup;
        await using FileStream setupLock = await AcquireLockAsync(Path.Combine(tools, "rust-setup.lock")).ConfigureAwait(false);
        if (!File.Exists(rustup))
        {
            string installer = await DownloadInstallerAsync(tools).ConfigureAwait(false);

            // Keep installer defaults private even when callers selected
            // another existing toolchain root or crate-cache location.
            var installerEnvironment = new Dictionary<string, string?>(NullableEnvironment(environment))
            {
                ["CARGO_HOME"] = Path.Combine(tools, "cargo"),
                ["RUSTUP_HOME"] = Path.Combine(tools, "rustup"),
            };
            await ProcessRunner.RunAsync(installer, ["-y", "--no-modify-path", "--profile", "minimal", "--default-toolchain", "none"], root, installerEnvironment).ConfigureAwait(false);
            rustup = privateRustup;
            if (!File.Exists(rustup))
            {
                throw new FileNotFoundException("The private Rust installer did not create rustup.", rustup);
            }
        }

        string? selectedSysroot = await ProbeAsync(rustup, toolchain, root, environment).ConfigureAwait(false);
        if (selectedSysroot is null)
        {
            var arguments = new List<string> { "toolchain", "install", toolchain, "--profile", "minimal", "--no-self-update" };
            if (wasm)
            {
                arguments.AddRange(["--component", "rust-src"]);
            }

            await ProcessRunner.RunAsync(rustup, arguments, root, NullableEnvironment(environment)).ConfigureAwait(false);
        }
        else if (wasm && !HasSources(selectedSysroot))
        {
            await ProcessRunner.RunAsync(rustup, ["component", "add", "rust-src", "--toolchain", toolchain], root, NullableEnvironment(environment)).ConfigureAwait(false);
        }

        string? ready = await ProbeAsync(rustup, toolchain, root, environment).ConfigureAwait(false);
        if (ready is null || (wasm && !HasSources(ready)))
        {
            throw new InvalidOperationException($"Rust toolchain '{toolchain}' was not installed successfully.");
        }

        return await CreateAsync(rustup, toolchain, tools, root, environment).ConfigureAwait(false);
    }

    private static bool HasSources(string sysroot) => File.Exists(Path.Combine(sysroot, "lib", "rustlib", "src", "rust", "library", "Cargo.toml"));

    private static string Executable(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;

    private static string QualifyToolchain(string toolchain)
    {
        bool official = toolchain is "stable" or "beta" or "nightly";
        int separator = toolchain.IndexOf('-');
        if (separator > 0 && toolchain[..separator] is "stable" or "beta" or "nightly")
        {
            official = DateOnly.TryParseExact(toolchain.AsSpan(separator + 1), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
        }

        official |= toolchain.Count(character => character == '.') == 2 && Version.TryParse(toolchain, out _);
        return official ? toolchain + "-" + HostTriple() : toolchain;
    }

    private static string HostTriple()
    {
        string architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x86_64",
            Architecture.Arm64 => "aarch64",
            _ => throw new PlatformNotSupportedException("Private Rust setup supports x64 and arm64 processes."),
        };
        string platform = OperatingSystem.IsWindows() ? "pc-windows-msvc" : OperatingSystem.IsLinux() ? "unknown-linux-gnu" : OperatingSystem.IsMacOS() ? "apple-darwin" : throw new PlatformNotSupportedException("Private Rust setup supports Windows, Linux and macOS.");
        return architecture + "-" + platform;
    }

    private static string? FindRustup(string? cargoHome)
    {
        if (!string.IsNullOrWhiteSpace(cargoHome))
        {
            string configured = Path.Combine(Path.GetFullPath(cargoHome), "bin", Executable("rustup"));
            if (File.Exists(configured))
            {
                return configured;
            }
        }

        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(directory.Trim('"'), Executable("rustup"));
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    private static async Task<RustToolchain> CreateAsync(string rustup, string toolchain, string tools, string workingDirectory, Dictionary<string, string> environment)
    {
        string directory = Path.GetDirectoryName(rustup)!;
        string cargo = Path.Combine(directory, Executable("cargo"));
        string rustc = Path.Combine(directory, Executable("rustc"));

        // The +toolchain argument is a rustup-shim convention, not a Cargo
        // convention. If sibling tools are missing, create private multicall
        // shims from the same verified/existing rustup executable.
        if (!File.Exists(cargo) || !File.Exists(rustc))
        {
            directory = Path.Combine(tools, "rust-shims");
            Directory.CreateDirectory(directory);
            cargo = Path.Combine(directory, Executable("cargo"));
            rustc = Path.Combine(directory, Executable("rustc"));
            CopyShim(rustup, cargo);
            CopyShim(rustup, rustc);
        }

        environment["PATH"] = directory + Path.PathSeparator + (Environment.GetEnvironmentVariable("PATH") ?? string.Empty);
        string version = await ProcessRunner.RunAsync(cargo, [$"+{toolchain}", "--version"], workingDirectory, NullableEnvironment(environment), captureOutput: true).ConfigureAwait(false);
        if (!version.StartsWith("cargo ", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("The resolved Cargo executable is not a compatible rustup shim.");
        }

        return new RustToolchain(cargo, rustc, rustup, toolchain, environment);
    }

    private static void CopyShim(string source, string destination)
    {
        string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.Copy(source, temporary);
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static IReadOnlyDictionary<string, string?> NullableEnvironment(IReadOnlyDictionary<string, string> environment)
        => environment.ToDictionary(pair => pair.Key, pair => (string?)pair.Value);

    private static async Task<string?> ProbeAsync(string rustup, string toolchain, string workingDirectory, IReadOnlyDictionary<string, string>? environment)
    {
        var start = new ProcessStartInfo(rustup)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (string argument in new[] { "run", toolchain, "rustc", "--print", "sysroot" })
        {
            start.ArgumentList.Add(argument);
        }

        if (environment is not null)
        {
            foreach ((string key, string value) in environment)
            {
                start.Environment[key] = value;
            }
        }

        using var process = new Process { StartInfo = start };
        try
        {
            if (!process.Start())
            {
                return null;
            }
        }
        catch (Exception exception) when (exception is Win32Exception or IOException)
        {
            return null;
        }

        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        await Task.WhenAll(stdout, stderr, process.WaitForExitAsync()).ConfigureAwait(false);
        string result = (await stdout.ConfigureAwait(false)).Trim();
        return process.ExitCode == 0 && Directory.Exists(result) ? result : null;
    }

    private static async Task<FileStream> AcquireLockAsync(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        DateTime deadline = DateTime.UtcNow.AddMinutes(10);
        while (true)
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                await Task.Delay(150).ConfigureAwait(false);
            }
        }
    }

    private static async Task<string> DownloadInstallerAsync(string tools)
    {
        using Stream metadata = typeof(RustToolchain).Assembly.GetManifestResourceStream("Lumyte.Text.Parley.Build.RustBootstrap.json") ?? throw new InvalidOperationException("Rust bootstrap metadata is missing.");
        BootstrapMetadata definition = await JsonSerializer.DeserializeAsync<BootstrapMetadata>(metadata, _jsonOptions).ConfigureAwait(false) ?? throw new InvalidOperationException("Rust bootstrap metadata is invalid.");
        string host = HostTriple();
        BootstrapAsset asset = definition.Targets.Values.Single(asset => asset.Triple == host);
        string installer = Path.Combine(tools, "downloads", $"rustup-init-{definition.Version}-{asset.Triple}" + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));
        Directory.CreateDirectory(Path.GetDirectoryName(installer)!);
        if (File.Exists(installer))
        {
            await using FileStream cached = File.OpenRead(installer);
            if (Convert.ToHexString(await SHA256.HashDataAsync(cached).ConfigureAwait(false)).Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                SetExecutable(installer);
                return installer;
            }
        }

        string temporary = installer + "." + Guid.NewGuid().ToString("N") + ".download";
        try
        {
            string name = OperatingSystem.IsWindows() ? "rustup-init.exe" : "rustup-init";
            string url = $"https://static.rust-lang.org/rustup/archive/{definition.Version}/{asset.Triple}/{name}";
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            using HttpResponseMessage response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            await using Stream download = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                byte[] buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await download.ReadAsync(buffer).ConfigureAwait(false)) != 0)
                {
                    total += read;
                    if (total > 100 * 1024 * 1024)
                    {
                        throw new InvalidDataException("The Rust installer exceeds the 100 MiB download limit.");
                    }

                    digest.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
                }
            }

            if (!Convert.ToHexString(digest.GetHashAndReset()).Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The Rust installer SHA-256 does not match the pinned release.");
            }

            SetExecutable(temporary);
            File.Move(temporary, installer, overwrite: true);
            return installer;
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static void SetExecutable(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        }
    }

    private sealed record BootstrapMetadata(string Version, Dictionary<string, BootstrapAsset> Targets);

    private sealed record BootstrapAsset(string Triple, string Sha256);
}
