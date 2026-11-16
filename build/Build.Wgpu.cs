using Fallout.Common;

namespace Lumyte.Build;

internal sealed partial class Build
{
    private Target WgpuNative => definition => definition
        .Executes(BuildWgpuNativeAsync);

    private async Task BuildWgpuNativeAsync()
    {
        string root = RepositoryDirectory;
        string output = Path.GetFullPath(Output ?? Path.Combine(root, "artifacts", "wgpu-native"));
        if ((Cargo is null) != (Rustc is null))
        {
            throw new ArgumentException("Supply both --cargo and --rustc when overriding Rust executables.");
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
            RustToolchain rust = await RustToolchain.ResolveAsync(root, NativeToolchain, wasm: false).ConfigureAwait(false);
            cargo = rust.CargoPath;
            rustc = rust.RustcPath;
            foreach ((string key, string value) in rust.EnvironmentOverrides)
            {
                environment[key] = value;
            }
        }

        environment["RUSTC"] = rustc;
        environment["PYTHONHOME"] = null;
        environment["PYTHONPATH"] = null;
        environment["PYTHONNOUSERSITE"] = "1";
        string python = await PythonRuntime.ResolveAsync(root, null, Python).ConfigureAwait(false);
        await ProcessRunner.RunAsync(python,
            [Path.Combine(root, "native", "wgpu_native", "build.py"), "--output", output, "--cargo", cargo],
            root, environment).ConfigureAwait(false);
    }
}
