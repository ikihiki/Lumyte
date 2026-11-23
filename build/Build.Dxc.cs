using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Lumyte.Build;

internal sealed partial class Build
{
    private async Task SetupDxcAsync()
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("SetupDxc installs the Linux Slang dependency.");
        }

        const string Commit = "b106a961d09221b3c5bdb37be45b679257da08b8"; // DXC v1.8.2505.1
        string root = Path.Combine(RequireEnvironmentValue("LUMYTE_ENV_ROOT"), "dxc");
        string library = Path.Combine(root, "lib");
        Dictionary<string, string> inputs = await DxcInputsAsync(Commit).ConfigureAwait(false);
        string fingerprint = DxcCache.Fingerprint(inputs);
        if (DxcCache.Verify(root, fingerprint))
        {
            Console.WriteLine("DXC verified cache hit: " + fingerprint);
            return;
        }

        Console.WriteLine("DXC cache miss: " + fingerprint);
        Directory.CreateDirectory(root);
        File.Delete(Path.Combine(root, "provenance.json"));
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
            // The pinned binary distribution supplies separate Microsoft/LLVM notices.
            // Preserve both in the installed license artifact covered by the manifest.
            File.WriteAllText(Path.Combine(root, "LICENSE.TXT"),
                File.ReadAllText(Path.Combine(root, "LICENSE-MS.txt")) + "\n" +
                File.ReadAllText(Path.Combine(root, "LICENSE-LLVM.txt")));
        }
        else if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
        {
            // Microsoft does not publish a Linux ARM64 archive. Build the same release,
            // without tests; this release always enables SPIR-V on Linux, so its
            // pinned dependencies are required even when Slang only emits DXIL.
            string source = Path.Combine(root, "source");
            string output = Path.Combine(root, "build");
            // Never trust old mutable source or CMake state on a cache miss.
            if (Directory.Exists(source))
            {
                Directory.Delete(source, recursive: true);
            }

            if (Directory.Exists(output))
            {
                Directory.Delete(output, recursive: true);
            }

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
            await ProcessRunner.RunAsync("cmake", ["-S", source, "-B", output, "-G", "Ninja", "-C", Path.Combine(source, "cmake", "caches", "PredefinedParams.cmake"), "-DCMAKE_C_COMPILER=" + ResolveDxcTool(Environment.GetEnvironmentVariable("CC") ?? "cc"), "-DCMAKE_CXX_COMPILER=" + ResolveDxcTool(Environment.GetEnvironmentVariable("CXX") ?? "c++"), "-DCMAKE_BUILD_TYPE=Release", "-DCMAKE_PROJECT_INCLUDE=" + Path.Combine(RootDirectory, "tools", "setup", "dxc-compiler-options.cmake"), "-DCMAKE_POLICY_VERSION_MINIMUM=3.5", "-DHLSL_DISABLE_SOURCE_GENERATION=ON", "-DHLSL_INCLUDE_TESTS=OFF", "-DLLVM_INCLUDE_TESTS=OFF", "-DENABLE_SPIRV_CODEGEN=ON", "-DSPIRV_BUILD_TESTS=OFF", "-DLLVM_ENABLE_ASSERTIONS=OFF", "-DLLVM_ENABLE_ZLIB=OFF", "-DHLSL_ENABLE_WERROR=OFF"], RootDirectory).ConfigureAwait(false);
            await ProcessRunner.RunAsync("cmake", ["--build", output, "--target", "dxcompiler", "dxildll", "--parallel", Math.Clamp(Environment.ProcessorCount, 1, 4).ToString(System.Globalization.CultureInfo.InvariantCulture)], RootDirectory).ConfigureAwait(false);
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

        DxcCache.Publish(root, inputs);
        if (!DxcCache.Verify(root, fingerprint))
        {
            throw new InvalidDataException("DXC installation failed verification.");
        }
    }

    private async Task<Dictionary<string, string>> DxcInputsAsync(string commit)
    {
        var inputs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["schema"] = "1",
            ["commit"] = commit, // Includes the release's pinned submodule gitlinks.
            ["architecture"] = RuntimeInformation.ProcessArchitecture.ToString(),
            ["osArchitecture"] = RuntimeInformation.OSArchitecture.ToString(),
            ["os"] = RuntimeInformation.OSDescription,
        };
        foreach (string file in new[] { "build/Build.Dxc.cs", "build/DxcCache.cs", "tools/setup/dxc-compiler-options.cmake", "mise.toml", "mise.lock" })
        {
            // Hashing the setup implementation includes every literal configure/build flag,
            // release archive URL/checksum and the selected upstream targets.
            inputs[file] = DxcCache.HashFile(Path.Combine(RootDirectory, file));
        }

        // Bind installed compiler support binaries, headers, linker and ABI packages.
        foreach (string file in new[] { "/etc/os-release", "/var/lib/dpkg/status" })
        {
            inputs[file] = DxcCache.HashFile(file);
        }

        string sysroot = Path.Combine(RequireEnvironmentValue("LUMYTE_ENV_ROOT"), "sysroot");
        inputs["sysroot"] = Directory.Exists(sysroot)
            ? DxcCache.Fingerprint(Directory.EnumerateFiles(sysroot, "*", SearchOption.AllDirectories)
                .Select(path => KeyValuePair.Create(Path.GetRelativePath(sysroot, path), DxcCache.HashFile(path))))
            : "absent";
        foreach (string name in new[] { "CC", "CXX", "CFLAGS", "CXXFLAGS", "CPPFLAGS", "LDFLAGS", "CPATH", "C_INCLUDE_PATH", "CPLUS_INCLUDE_PATH", "LIBRARY_PATH", "LD_LIBRARY_PATH", "COMPILER_PATH", "GCC_EXEC_PREFIX", "SOURCE_DATE_EPOCH", "CMAKE_TOOLCHAIN_FILE", "CMAKE_PREFIX_PATH", "SDKROOT" })
        {
            string value = Environment.GetEnvironmentVariable(name) ?? "";
            if (name == "LD_LIBRARY_PATH")
            {
                // Activation adds our DXC output and the already-hashed private sysroot.
                string environmentRoot = RequireEnvironmentValue("LUMYTE_ENV_ROOT");
                value = string.Join(Path.PathSeparator, value.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                    .Where(path => Path.GetFullPath(path) != Path.Combine(environmentRoot, "dxc", "lib") &&
                        !Path.GetFullPath(path).StartsWith(Path.Combine(environmentRoot, "sysroot") + Path.DirectorySeparatorChar, StringComparison.Ordinal)));
            }

            inputs["env:" + name] = value;
            // Arbitrary external toolchains/include paths cannot be safely captured by
            // a version string. Refuse cache reuse (but preserve normal compilation).
            if (value.Length > 0 && name is not "SOURCE_DATE_EPOCH")
            {
                inputs["custom-environment-no-reuse"] = Guid.NewGuid().ToString("N");
            }
        }

        if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64)
        {
            // A fresh verified hit starts neither git nor cmake. Custom compilers
            // or mutable external include/flag inputs deliberately cannot hit.
            foreach (string tool in new[] { Environment.GetEnvironmentVariable("CC") ?? "cc", Environment.GetEnvironmentVariable("CXX") ?? "c++", "cmake", "ninja", "ld", "ar", "as" })
            {
                string path = ResolveDxcTool(tool);
                inputs["tool:" + tool] = path + ":" + DxcCache.HashFile(path);
            }

            foreach (string compiler in new[] { Environment.GetEnvironmentVariable("CC") ?? "cc", Environment.GetEnvironmentVariable("CXX") ?? "c++" })
            {
                foreach (string query in new[] { "--version", "-dumpmachine", "-print-search-dirs", "-print-sysroot" })
                {
                    inputs[compiler + query] = await ProcessRunner.RunAsync(ResolveDxcTool(compiler), [query], RootDirectory, captureOutput: true).ConfigureAwait(false);
                }

                foreach (string dependency in new[] { "cc1", "cc1plus", "collect2", "libgcc.a", "libgcc_s.so.1", "libstdc++.so", "libc.so", "crtbegin.o" })
                {
                    string path = (await ProcessRunner.RunAsync(ResolveDxcTool(compiler), ["-print-file-name=" + dependency], RootDirectory, captureOutput: true).ConfigureAwait(false)).Trim();
                    if (File.Exists(path))
                    {
                        inputs[compiler + ":" + dependency] = DxcCache.HashFile(path);
                    }
                }
            }
        }

        return inputs;
    }

    private static string ResolveDxcTool(string name)
    {
        string? path = Path.IsPathRooted(name) ? name :
            (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
                .Select(directory => Path.GetFullPath(Path.Combine(directory, name))).FirstOrDefault(File.Exists);
        if (path is null || !File.Exists(path))
        {
            throw new FileNotFoundException("DXC toolchain executable is missing: " + name);
        }

        return new FileInfo(path).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? Path.GetFullPath(path);
    }
}
