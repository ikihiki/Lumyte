using Fallout.Common;

namespace Lumyte.Build;

internal sealed partial class Build
{
    private Target TestCefSupervisor => definition => definition.Executes(async () =>
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("The browser-local cleanup owner requires Linux.");
        }

        string root = RepositoryDirectory;
        RustToolchain rust = await RustToolchain.ResolveAsync(root, "1.99.1", false).ConfigureAwait(false);
        var environment = rust.EnvironmentOverrides.ToDictionary(pair => pair.Key, pair => (string?)pair.Value);
        await ProcessRunner.RunAsync(rust.CargoPath, [$"+{rust.Toolchain}", "test", "--locked", "--offline", "--release", "--manifest-path", Path.Combine(root, "src/UI/Lumyte.UI.WebView.Cef.Supervisor/Cargo.toml"), "--target-dir", Path.Combine(root, "artifacts/cef-supervisor/cargo"), "--", "--test-threads=1"], root, environment).ConfigureAwait(false);
    });

    private Target CefSupervisor => definition => definition.Executes(async () =>
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("The browser-local cleanup owner requires Linux.");
        }

        string root = RepositoryDirectory;
        RustToolchain rust = await RustToolchain.ResolveAsync(root, "1.99.1", false).ConfigureAwait(false);
        var environment = rust.EnvironmentOverrides.ToDictionary(pair => pair.Key, pair => (string?)pair.Value);
        environment["RUSTC"] = rust.RustcPath;
        string target = Path.Combine(root, "artifacts", "cef-supervisor", "cargo");
        await ProcessRunner.RunAsync(rust.CargoPath, [$"+{rust.Toolchain}", "build", "--locked", "--offline", "--release", "--manifest-path", Path.Combine(root, "src/UI/Lumyte.UI.WebView.Cef.Supervisor/Cargo.toml"), "--target-dir", target], root, environment).ConfigureAwait(false);
        string output = Path.GetFullPath(Output ?? Path.Combine(root, "artifacts", "cef-supervisor", "native"));
        Directory.CreateDirectory(output);
        File.Copy(Path.Combine(target, "release", "lumyte-cef-supervisor"), Path.Combine(output, "lumyte-cef-supervisor"), true);
        File.SetUnixFileMode(Path.Combine(output, "lumyte-cef-supervisor"), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    });
}
