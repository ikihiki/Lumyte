using System.Text;
using Fallout.Common;

namespace Lumyte.Text.Parley.Build;

internal sealed class Build : FalloutBuild
{
    private const string NativeToolchain = "1.99.1";
    private const string WasmToolchain = "nightly-2026-10-20";

    [Parameter("Output directory for native libraries and Cargo intermediates.")]
    public string? Output { get; set; }

    [Parameter("Rust toolchain override.")]
    public string? Toolchain { get; set; }

    [Parameter("Emscripten SDK tools directory from the .NET wasm-tools workload.")]
    public string? Emsdk { get; set; }

    [Parameter("Emscripten cache from the .NET wasm-tools workload.")]
    public string? Cache { get; set; }

    [Parameter("Node.js executable from the .NET wasm-tools workload.")]
    public string? Node { get; set; }

    [Parameter("Explicit Python executable; otherwise a private verified runtime is provisioned.")]
    public string? Python { get; set; }

    [Parameter("File receiving the resolved private Python executable for MSBuild.")]
    public string? PythonPathFile { get; set; }

    [Parameter("Cargo executable override; specify Rustc as well to use an existing compiler directly.")]
    public string? Cargo { get; set; }

    [Parameter("Rustc executable override paired with Cargo.")]
    public string? Rustc { get; set; }

    [Parameter("Rustup executable override for explicit setup targets.")]
    public string? Rustup { get; set; }

    private Target Native => definition => definition
        .Executes(() => BuildAsync(wasm: false));

    private Target Wasm => definition => definition
        .Executes(() => BuildAsync(wasm: true));

    private Target SetupNative => definition => definition
        .Executes(() => SetupAsync(wasm: false));

    private Target SetupWasm => definition => definition
        .Executes(() => SetupAsync(wasm: true));

    private Target TestRust => definition => definition
        .Executes(RunRustTestsAsync);

    private Target PrepareWasmTools => definition => definition
        .Executes(async () =>
        {
            if (string.IsNullOrWhiteSpace(PythonPathFile))
            {
                throw new ArgumentException("PrepareWasmTools requires --python-path-file.");
            }

            string python = await PythonRuntime.ResolveAsync(RootDirectory, Emsdk, Python).ConfigureAwait(false);
            string destination = Path.GetFullPath(PythonPathFile);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            await File.WriteAllTextAsync(destination, python + Environment.NewLine, new UTF8Encoding(false)).ConfigureAwait(false);
            Console.WriteLine($"Emscripten Python: {python}");
        });

    public static int Main() => Execute<Build>(build => build.Native);

    private static string FindExecutable(string name)
    {
        string filename = name + (OperatingSystem.IsWindows() ? ".exe" : string.Empty);
        foreach (string directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
        {
            string path = Path.Combine(directory, filename);
            if (File.Exists(path))
            {
                return path;
            }
        }

        throw new FileNotFoundException($"{name} must be on PATH or supplied explicitly.");
    }

    private async Task SetupAsync(bool wasm)
    {
        string selected = Toolchain ?? (wasm ? WasmToolchain : NativeToolchain);
        if (Rustup is string rustup)
        {
            var arguments = new List<string> { "toolchain", "install", selected, "--profile", "minimal" };
            if (wasm)
            {
                arguments.AddRange(["--component", "rust-src"]);
            }

            await ProcessRunner.RunAsync(rustup, arguments, RootDirectory).ConfigureAwait(false);
        }
        else
        {
            await RustToolchain.ResolveAsync(RootDirectory, selected, wasm).ConfigureAwait(false);
        }
    }

    private async Task RunRustTestsAsync()
    {
        if ((Cargo is null) != (Rustc is null))
        {
            throw new ArgumentException("Supply both --cargo and --rustc when overriding the Rust executables.");
        }

        string root = RootDirectory;
        string selected = Toolchain ?? NativeToolchain;
        string cargo;
        var environment = new Dictionary<string, string?>();
        if (Cargo is not null)
        {
            cargo = Cargo;
        }
        else
        {
            RustToolchain rust = await RustToolchain.ResolveAsync(root, selected, wasm: false).ConfigureAwait(false);
            selected = rust.Toolchain;
            cargo = rust.CargoPath;
            foreach ((string key, string value) in rust.EnvironmentOverrides)
            {
                environment[key] = value;
            }
        }

        string manifest = Path.Combine(root, "native", "lumyte_text_parley", "Cargo.toml");
        string output = Path.GetFullPath(Output ?? Path.Combine(root, "artifacts", "text-parley", "tests"));
        await ProcessRunner.RunAsync(cargo, [$"+{selected}", "test", "--manifest-path", manifest, "--locked", "--target-dir", Path.Combine(output, "cargo")], root, environment).ConfigureAwait(false);
    }

    private async Task BuildAsync(bool wasm)
    {
        string root = RootDirectory;
        string selected = Toolchain ?? (wasm ? WasmToolchain : NativeToolchain);
        string output = Path.GetFullPath(Output ?? Path.Combine(root, "artifacts", "text-parley", wasm ? "wasm" : "native"));
        string manifest = Path.Combine(root, "native", "lumyte_text_parley", "Cargo.toml");
        if (!File.Exists(manifest))
        {
            throw new FileNotFoundException("The repository root does not contain the Parley Cargo manifest.", manifest);
        }

        if ((Cargo is null) != (Rustc is null))
        {
            throw new ArgumentException("Supply both --cargo and --rustc when overriding the Rust executables.");
        }

        var environment = new Dictionary<string, string?>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        string cargo;
        string rustc;
        if (Cargo is not null && Rustc is not null)
        {
            cargo = Cargo;
            rustc = Rustc;
        }
        else
        {
            RustToolchain rust = await RustToolchain.ResolveAsync(root, selected, wasm).ConfigureAwait(false);
            selected = rust.Toolchain;
            cargo = rust.CargoPath;
            rustc = rust.RustcPath;
            foreach ((string key, string value) in rust.EnvironmentOverrides)
            {
                environment[key] = value;
            }
        }

        string cargoOutput = Path.Combine(output, "cargo");
        Directory.CreateDirectory(output);
        var arguments = new List<string> { $"+{selected}" };
        string source;
        string destination;
        if (wasm)
        {
            await ConfigureWasmAsync(root, environment).ConfigureAwait(false);
            arguments.AddRange(["rustc", "-Zbuild-std=std,panic_abort", "--target", "wasm32-unknown-emscripten", "--crate-type", "staticlib"]);
            source = Path.Combine(cargoOutput, "wasm32-unknown-emscripten", "release", "liblumyte_text_parley.a");
            destination = Path.Combine(output, "liblumyte_text_parley.a");
        }
        else
        {
            arguments.Add("build");
            string filename = OperatingSystem.IsWindows() ? "lumyte_text_parley.dll" : OperatingSystem.IsMacOS() ? "liblumyte_text_parley.dylib" : "liblumyte_text_parley.so";
            source = Path.Combine(cargoOutput, "release", filename);
            destination = Path.Combine(output, filename);
        }

        arguments.AddRange(["--manifest-path", manifest, "--locked", "--release", "--target-dir", cargoOutput]);
        await ProcessRunner.RunAsync(cargo, arguments, root, environment).ConfigureAwait(false);
        if (!File.Exists(source))
        {
            throw new FileNotFoundException("Cargo did not produce the expected library.", source);
        }

        string sysroot = (await ProcessRunner.RunAsync(rustc, [$"+{selected}", "--print", "sysroot"], root, environment, captureOutput: true).ConfigureAwait(false)).Trim();
        string notices = Path.Combine(sysroot, "share", "doc", "rust", "COPYRIGHT-library.html");
        if (!File.Exists(notices))
        {
            throw new FileNotFoundException("The selected Rust toolchain's runtime license notices are missing.", notices);
        }

        File.Copy(source, destination, overwrite: true);
        File.Copy(notices, Path.Combine(output, "RUST-STANDARD-LIBRARY-NOTICES.html"), overwrite: true);
        Console.WriteLine(destination);
    }

    private async Task ConfigureWasmAsync(string root, Dictionary<string, string?> environment)
    {
        if (string.IsNullOrWhiteSpace(Emsdk) || string.IsNullOrWhiteSpace(Cache))
        {
            throw new ArgumentException("Wasm requires --emsdk and --cache from the .NET wasm-tools workload.");
        }

        string sdk = Path.GetFullPath(Emsdk);
        string emscripten = Path.Combine(sdk, "emscripten");
        string node = Path.GetFullPath(Node ?? FindExecutable("node"));
        if (!File.Exists(Path.Combine(emscripten, "emcc.py")) || !File.Exists(node))
        {
            throw new ArgumentException("A valid .NET Emscripten SDK and Node.js executable are required.");
        }

        string python = await PythonRuntime.ResolveAsync(root, sdk, Python).ConfigureAwait(false);
        string inheritedPath = environment.GetValueOrDefault("PATH") ?? Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        environment["DOTNET_EMSCRIPTEN_LLVM_ROOT"] = Path.Combine(sdk, "bin");
        environment["DOTNET_EMSCRIPTEN_BINARYEN_ROOT"] = sdk;
        environment["DOTNET_EMSCRIPTEN_NODE_JS"] = node;
        environment["EM_CONFIG"] = Path.Combine(emscripten, ".emscripten");
        environment["EM_CACHE"] = Path.GetFullPath(Cache);
        environment["EM_FROZEN_CACHE"] = "1";
        environment["EMSDK_PYTHON"] = python;
        environment["PYTHON"] = python;
        environment["PYTHON3"] = python;
        environment["PYTHONHOME"] = null;
        environment["PYTHONPATH"] = null;
        environment["PYTHONNOUSERSITE"] = "1";
        environment["PATH"] = string.Join(Path.PathSeparator, Path.GetDirectoryName(python), emscripten, Path.Combine(sdk, "bin"), inheritedPath);
        string suffix = OperatingSystem.IsWindows() ? ".bat" : string.Empty;
        environment["CC_wasm32_unknown_emscripten"] = Path.Combine(emscripten, "emcc" + suffix);
        environment["AR_wasm32_unknown_emscripten"] = Path.Combine(emscripten, "emar" + suffix);
        environment["CARGO_TARGET_WASM32_UNKNOWN_EMSCRIPTEN_LINKER"] = environment["CC_wasm32_unknown_emscripten"];
        string? flags = Environment.GetEnvironmentVariable("CARGO_ENCODED_RUSTFLAGS");
        if (string.IsNullOrEmpty(flags) && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RUSTFLAGS")))
        {
            throw new ArgumentException("Use CARGO_ENCODED_RUSTFLAGS instead of RUSTFLAGS for the wasm build.");
        }

        environment["RUSTFLAGS"] = null;
        environment["CARGO_ENCODED_RUSTFLAGS"] = string.Join('\u001f', new[] { flags, "-Cpanic=abort", "-Zllvm-target-feature=-bulk-memory-opt,-call-indirect-overlong" }.Where(value => !string.IsNullOrEmpty(value)));
        environment["CARGO_PROFILE_RELEASE_LTO"] = "false";
    }
}
