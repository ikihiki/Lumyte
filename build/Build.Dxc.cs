using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Fallout.Common;

namespace Lumyte.Build;

internal sealed partial class Build
{
    private Target SetupDxc => definition => definition
        .Executes(SetupDxcAsync);

    private async Task SetupDxcAsync()
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("SetupDxc installs the Linux Slang dependency.");
        }

        const string Commit = "b106a961d09221b3c5bdb37be45b679257da08b8"; // DXC v1.8.2505.1
        string root = Path.Combine(RequireEnvironmentValue("LUMYTE_ENV_ROOT"), "dxc");
        string stamp = Path.Combine(root, "version.txt");
        string version = Commit + ":" + RuntimeInformation.ProcessArchitecture;
        string library = Path.Combine(root, "lib");
        if (File.Exists(stamp) && await File.ReadAllTextAsync(stamp).ConfigureAwait(false) == version &&
            File.Exists(Path.Combine(library, "libdxcompiler.so")) && File.Exists(Path.Combine(library, "libdxil.so")))
        {
            return;
        }

        Directory.CreateDirectory(root);
        if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
        {
            const string Url = "https://github.com/microsoft/DirectXShaderCompiler/releases/download/v1.8.2505.1/linux_dxc_2025_07_14.x86_64.tar.gz";
            const string Checksum = "f2213da1fc99dc8778c8823078e16ba97c7f80f86a1d4520ab1adf4b462bc48c";
            string archive = Path.Combine(root, "dxc.tar.gz");
            await ProcessRunner.RunAsync("curl", ["--fail", "--location", "--retry", "3", "--proto", "=https", "--tlsv1.2", Url, "--output", archive], RootDirectory).ConfigureAwait(false);
            await using (FileStream input = File.OpenRead(archive))
            {
                byte[] digest = await SHA256.HashDataAsync(input).ConfigureAwait(false);
                if (!Convert.ToHexString(digest).Equals(Checksum, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException("DXC archive does not match its pinned SHA-256.");
                }
            }

            await ProcessRunner.RunAsync("tar", ["-xzf", archive, "-C", root], RootDirectory).ConfigureAwait(false);
        }
        else if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
        {
            // Microsoft does not publish a Linux ARM64 archive. Build the same release,
            // without tests; this release always enables SPIR-V on Linux, so its
            // pinned dependencies are required even when Slang only emits DXIL.
            string source = Path.Combine(root, "source");
            string output = Path.Combine(root, "build");
            Directory.CreateDirectory(source);
            if (!Directory.Exists(Path.Combine(source, ".git")))
            {
                await ProcessRunner.RunAsync("git", ["init", source], RootDirectory).ConfigureAwait(false);
            }

            await ProcessRunner.RunAsync("git", ["-C", source, "fetch", "--depth", "1", "https://github.com/microsoft/DirectXShaderCompiler.git", Commit], RootDirectory).ConfigureAwait(false);
            await ProcessRunner.RunAsync("git", ["-C", source, "checkout", "--detach", Commit], RootDirectory).ConfigureAwait(false);
            await ProcessRunner.RunAsync("git", ["-C", source, "submodule", "update", "--init", "--depth", "1", "--", "external/DirectX-Headers", "external/SPIRV-Headers", "external/SPIRV-Tools"], RootDirectory).ConfigureAwait(false);
            // Use the release's checked-in generated headers; regeneration compares against
            // a specific upstream clang-format version, not our development formatter.
            await ProcessRunner.RunAsync("cmake", ["-S", source, "-B", output, "-G", "Ninja", "-C", Path.Combine(source, "cmake", "caches", "PredefinedParams.cmake"), "-DCMAKE_BUILD_TYPE=Release", "-DCMAKE_POLICY_VERSION_MINIMUM=3.5", "-DHLSL_DISABLE_SOURCE_GENERATION=ON", "-DHLSL_INCLUDE_TESTS=OFF", "-DLLVM_INCLUDE_TESTS=OFF", "-DENABLE_SPIRV_CODEGEN=ON", "-DSPIRV_BUILD_TESTS=OFF", "-DLLVM_ENABLE_ASSERTIONS=OFF", "-DLLVM_ENABLE_ZLIB=OFF", "-DHLSL_ENABLE_WERROR=OFF"], RootDirectory).ConfigureAwait(false);
            await ProcessRunner.RunAsync("cmake", ["--build", output, "--target", "dxcompiler", "dxildll", "--parallel", "2"], RootDirectory).ConfigureAwait(false);
            Directory.CreateDirectory(library);
            foreach (string name in new[] { "libdxcompiler.so", "libdxil.so" })
            {
                File.Copy(Path.Combine(output, "lib", name), Path.Combine(library, name), overwrite: true);
            }

            File.Copy(Path.Combine(source, "LICENSE.TXT"), Path.Combine(root, "LICENSE.TXT"), overwrite: true);
        }
        else
        {
            throw new PlatformNotSupportedException("DXC setup supports Linux x64 and ARM64.");
        }

        await File.WriteAllTextAsync(stamp, version).ConfigureAwait(false);
    }
}
