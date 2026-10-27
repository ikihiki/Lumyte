using System.Buffers.Binary;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Fallout.Common;

namespace Lumyte.Build;

internal sealed partial class Build
{
    private Target VerifyEnvironment => definition => definition
        .DependsOn(CheckNativeFormat, CheckMarkdown, NativeSmoke);

    private Target VerifySdk => definition => definition
        .Executes(VerifySdkAsync);

    private Target ConfigureLavapipe => definition => definition
        .Executes(ConfigureLavapipeManifest);

    private Target NativeSmoke => definition => definition
        .Executes(RunNativeSmokeAsync);

    private async Task VerifySdkAsync()
    {
        using var global = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(RootDirectory, "global.json")).ConfigureAwait(false));
        string expected = global.RootElement.GetProperty("sdk").GetProperty("version").GetString()!;
        string actual = await CaptureEnvironmentToolAsync(DotNetExecutable, ["--version"]).ConfigureAwait(false);
        string installed = await CaptureEnvironmentToolAsync("mise", ["current", "http:dotnet"]).ConfigureAwait(false);
        if (actual.Trim() != expected || installed.Trim() != expected)
        {
            throw new InvalidOperationException("The active .NET SDK, mise.toml and global.json versions differ.");
        }
    }

    private void ConfigureLavapipeManifest()
    {
        if (!OperatingSystem.IsLinux())
        {
            throw new PlatformNotSupportedException("ConfigureLavapipe configures Linux; Windows setup configures its verified Mesa archive.");
        }

        string root = RequireEnvironmentValue("LUMYTE_ENV_ROOT");
        string local = Path.Combine(root, "sysroot", "usr", "share", "vulkan", "icd.d");
        string[] manifests = Directory.Exists(local) ? Directory.GetFiles(local, "*lvp*.json") : [];
        bool relocated = manifests.Length > 0;
        if (!relocated)
        {
            const string SystemManifestRoot = "/usr/share/vulkan/icd.d";
            manifests = Directory.Exists(SystemManifestRoot) ? Directory.GetFiles(SystemManifestRoot, "*lvp*.json") : [];
        }

        if (manifests.Length != 1)
        {
            throw new InvalidOperationException($"Expected one lavapipe ICD; found {manifests.Length}.");
        }

        JsonNode manifest = JsonNode.Parse(File.ReadAllText(manifests[0])) ?? throw new InvalidDataException("The lavapipe ICD manifest is empty.");
        if (relocated)
        {
            string libraryRoot = Path.Combine(root, "sysroot", "usr", "lib");
            string[] libraries = Directory.Exists(libraryRoot) ? Directory.GetFiles(libraryRoot, "libvulkan_lvp.so", SearchOption.AllDirectories) : [];
            if (libraries.Length != 1)
            {
                throw new InvalidOperationException("Cannot locate the extracted lavapipe library.");
            }

            JsonObject icd = manifest["ICD"]?.AsObject() ?? throw new InvalidDataException("The lavapipe ICD record is missing.");
            icd["library_path"] = Path.GetFullPath(libraries[0]);
        }

        Directory.CreateDirectory(root);
        string destination = Path.Combine(root, "lavapipe.json");
        File.WriteAllText(destination, manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        Console.WriteLine($"Configured lavapipe ICD: {destination}");
    }

    private async Task RunNativeSmokeAsync()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("The native development environment smoke test supports Linux and Windows.");
        }

        string architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => throw new PlatformNotSupportedException("The native development environment requires x64 or arm64."),
        };
        if (OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture != RuntimeInformation.OSArchitecture)
        {
            throw new PlatformNotSupportedException("Use a native Windows shell instead of an emulated architecture.");
        }

        await VerifySdkAsync().ConfigureAwait(false);
        await VerifyNativeToolsAsync().ConfigureAwait(false);
        string environmentRoot = RequireEnvironmentValue("LUMYTE_ENV_ROOT");
        string vcpkg = RequireEnvironmentValue("VCPKG_ROOT");
        string feed = RequireEnvironmentValue("LUMYTE_NUGET_FEED");
        string icd = RequireEnvironmentValue("LUMYTE_LAVAPIPE_ICD");
        if (!File.Exists(icd))
        {
            throw new FileNotFoundException("The lavapipe ICD is missing.", icd);
        }

        Directory.CreateDirectory(feed);
        var environment = new Dictionary<string, string?>
        {
            ["VK_DRIVER_FILES"] = icd,
            ["VK_ICD_FILENAMES"] = icd,
        };
        if (OperatingSystem.IsWindows())
        {
            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(icd).ConfigureAwait(false));
            string library = manifest.RootElement.GetProperty("ICD").GetProperty("library_path").GetString() ?? throw new InvalidDataException("The lavapipe library path is missing.");
            string mesa = Path.GetDirectoryName(library) ?? throw new InvalidDataException("The lavapipe library directory is missing.");
            environment["PATH"] = mesa + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
        }
        else
        {
            string runtime = Path.Combine(environmentRoot, "xdg-runtime");
            Directory.CreateDirectory(runtime);
            File.SetUnixFileMode(runtime, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            environment["XDG_RUNTIME_DIR"] = runtime;
            await ToolWithEnvironmentAsync("vulkaninfo", environment, "--summary").ConfigureAwait(false);
        }

        string smoke = Path.Combine(environmentRoot, "smoke." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture));
        try
        {
            CopySmokeDirectory(Path.Combine(RootDirectory, "tools", "setup", "smoke"), smoke);
            foreach (string config in new[] { "Directory.Build.props", ".editorconfig", "stylecop.json" })
            {
                File.Copy(Path.Combine(RootDirectory, config), Path.Combine(smoke, config));
            }

            string shader = Path.Combine(smoke, "shader.spv");
            await ToolWithEnvironmentAsync("slangc", environment, Path.Combine(smoke, "shader.slang"), "-entry", "main", "-stage", "compute", "-target", "spirv", "-o", shader).ConfigureAwait(false);
            byte[] spirv = await File.ReadAllBytesAsync(shader).ConfigureAwait(false);
            if (spirv.Length <= 20 || spirv.Length % 4 != 0 || BinaryPrimitives.ReadUInt32LittleEndian(spirv) != 0x07230203)
            {
                throw new InvalidDataException("Slang produced invalid SPIR-V.");
            }

            string triplet = architecture + (OperatingSystem.IsWindows() ? "-windows-static" : "-linux");
            string rid = (OperatingSystem.IsWindows() ? "win-" : "linux-") + architecture;
            string nativeBuild = Path.Combine(smoke, "build");
            var cmake = new List<string>
            {
                "-S", smoke, "-B", nativeBuild, "-G", "Ninja",
                "-DCMAKE_TOOLCHAIN_FILE=" + Path.Combine(vcpkg, "scripts", "buildsystems", "vcpkg.cmake"),
                "-DVCPKG_TARGET_TRIPLET=" + triplet,
                "-DCMAKE_BUILD_TYPE=Release",
            };
            if (OperatingSystem.IsWindows())
            {
                cmake.Add("-DVCPKG_HOST_TRIPLET=" + triplet);
            }
            else if (Environment.GetEnvironmentVariable("LUMYTE_VULKAN_INCLUDE_DIR") is string include)
            {
                cmake.Add("-DVulkan_INCLUDE_DIR=" + include);
                cmake.Add("-DVulkan_LIBRARY=" + RequireEnvironmentValue("LUMYTE_VULKAN_LIBRARY"));
            }

            await ToolWithEnvironmentAsync("cmake", environment, cmake.ToArray()).ConfigureAwait(false);
            await ToolWithEnvironmentAsync("cmake", environment, "--build", nativeBuild, "--parallel", "2").ConfigureAwait(false);
            if (OperatingSystem.IsWindows())
            {
                string loader = Path.Combine(nativeBuild, "vcpkg_installed", triplet, "bin", "vulkan-1.dll");
                File.Copy(loader, Path.Combine(nativeBuild, "vulkan-1.dll"));
            }

            string version = "0.0.0-smoke." + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture) + "." + Environment.ProcessId.ToString(CultureInfo.InvariantCulture);
            string nativeProject = Path.Combine(smoke, "Native", "Native.csproj");
            string managedProject = Path.Combine(smoke, "Managed", "Managed.csproj");
            string nugetConfig = Path.Combine(smoke, "NuGet.config");
            new XDocument(new XElement("configuration", new XElement("packageSources",
                new XElement("clear"),
                new XElement("add", new XAttribute("key", "lumyte-smoke"), new XAttribute("value", feed)),
                new XElement("add", new XAttribute("key", "nuget.org"), new XAttribute("value", "https://api.nuget.org/v3/index.json")))))
                .Save(nugetConfig);
            await DotNetWithEnvironmentAsync(environment, "restore", nativeProject, "-warnaserror", "--configfile", nugetConfig, "-p:RuntimeIdentifier=" + rid).ConfigureAwait(false);
            await DotNetWithEnvironmentAsync(environment, "pack", nativeProject, "--configuration", "Release", "--no-restore", "-warnaserror", "--output", feed, "-p:PackageVersion=" + version, "-p:RuntimeIdentifier=" + rid).ConfigureAwait(false);
            await DotNetWithEnvironmentAsync(environment, "restore", managedProject, "-warnaserror", "--configfile", nugetConfig, "-p:SmokeVersion=" + version).ConfigureAwait(false);
            await DotNetWithEnvironmentAsync(environment, "format", "whitespace", managedProject, "--verify-no-changes", "--no-restore").ConfigureAwait(false);
            await DotNetWithEnvironmentAsync(environment, "format", "style", managedProject, "--verify-no-changes", "--no-restore", "--severity", "warn").ConfigureAwait(false);
            await DotNetWithEnvironmentAsync(environment, "build", managedProject, "--configuration", "Release", "--no-restore", "-warnaserror", "-p:SmokeVersion=" + version, "--no-self-contained").ConfigureAwait(false);
            await DotNetWithEnvironmentAsync(environment, "run", "--project", managedProject, "--configuration", "Release", "--no-build", "--no-restore", "-p:SmokeVersion=" + version, "--no-self-contained").ConfigureAwait(false);
            Console.WriteLine("PASS: Slang SPIR-V, C#, C++, vcpkg, native NuGet/PInvoke, lavapipe Vulkan readback, and Windows Direct3D 12 WARP where applicable.");
        }
        finally
        {
            if (Directory.Exists(smoke))
            {
                Directory.Delete(smoke, recursive: true);
            }
        }
    }

    private async Task VerifyNativeToolsAsync()
    {
        string expectedCmake = (await CaptureEnvironmentToolAsync("mise", ["current", "http:cmake"]).ConfigureAwait(false)).Trim();
        string expectedNinja = (await CaptureEnvironmentToolAsync("mise", ["current", "http:ninja"]).ConfigureAwait(false)).Trim();
        string actualCmake = (await CaptureEnvironmentToolAsync("cmake", ["--version"]).ConfigureAwait(false)).Split('\n')[0].Trim();
        string actualNinja = (await CaptureEnvironmentToolAsync("ninja", ["--version"]).ConfigureAwait(false)).Trim();
        if (actualCmake != "cmake version " + expectedCmake || actualNinja != expectedNinja)
        {
            throw new InvalidOperationException("The CMake or Ninja version differs from the pinned mise configuration.");
        }

        string vcpkg = RequireEnvironmentValue("VCPKG_ROOT");
        string commit = (await CaptureEnvironmentToolAsync("git", ["-c", "safe.directory=" + vcpkg, "-C", vcpkg, "rev-parse", "HEAD"]).ConfigureAwait(false)).Trim();
        if (commit != RequireEnvironmentValue("LUMYTE_VCPKG_COMMIT"))
        {
            throw new InvalidOperationException("The vcpkg checkout differs from its pinned commit.");
        }
    }

    private Task<string> CaptureEnvironmentToolAsync(string executable, IEnumerable<string> arguments)
        => ProcessRunner.RunAsync(executable, arguments, RootDirectory, captureOutput: true);

    private static string RequireEnvironmentValue(string name)
        => Environment.GetEnvironmentVariable(name) is string value && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new InvalidOperationException($"{name} is missing. Activate the repository development environment first.");

    private static void CopySmokeDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }

        foreach (string directory in Directory.GetDirectories(source))
        {
            CopySmokeDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)));
        }
    }
}
