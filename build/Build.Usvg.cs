namespace Lumyte.Build;

internal sealed partial class Build
{
    private async Task BuildUsvgNativeAsync()
    {
        string root = RepositoryDirectory;
        string output = Path.GetFullPath(Output ?? Path.Combine(root, "artifacts", "svg-usvg", "native"));
        RustToolchain rust = await RustToolchain.ResolveAsync(root, Toolchain ?? NativeToolchain, wasm: false).ConfigureAwait(false);
        var environment = rust.EnvironmentOverrides.ToDictionary(pair => pair.Key, pair => (string?)pair.Value);
        string cargoOutput = Path.Combine(output, "cargo");
        await ProcessRunner.RunAsync(rust.CargoPath, [$"+{rust.Toolchain}", "build", "--manifest-path", Path.Combine(root, "native", "lumyte_svg_usvg", "Cargo.toml"), "--locked", "--release", "--target-dir", cargoOutput], root, environment).ConfigureAwait(false);
        string filename = OperatingSystem.IsWindows() ? "lumyte_svg_usvg.dll" : OperatingSystem.IsMacOS() ? "liblumyte_svg_usvg.dylib" : "liblumyte_svg_usvg.so";
        File.Copy(Path.Combine(cargoOutput, "release", filename), Path.Combine(output, filename), overwrite: true);
        string sysroot = (await ProcessRunner.RunAsync(rust.RustcPath, [$"+{rust.Toolchain}", "--print", "sysroot"], root, environment, captureOutput: true).ConfigureAwait(false)).Trim();
        File.Copy(Path.Combine(sysroot, "share", "doc", "rust", "COPYRIGHT-library.html"), Path.Combine(output, "SVG-RUST-STANDARD-LIBRARY-NOTICES.html"), overwrite: true);
    }
}
