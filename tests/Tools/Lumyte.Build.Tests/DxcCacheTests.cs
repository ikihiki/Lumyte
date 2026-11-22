using System.Reflection;
using Xunit;

namespace Lumyte.Build.Tests;

/// <summary>Deterministic installed-DXC integrity and provenance contracts.</summary>
public sealed class DxcCacheTests
{
    private static Type Cache => Assembly.LoadFrom(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "runner-path.txt")).Trim()).GetType("Lumyte.Build.DxcCache", throwOnError: true)!;

    /// <summary>Only a complete, matching, unmodified installation can hit.</summary>
    /// <param name="mutation">The invalidation under test.</param>
    [Theory]
    [InlineData("none")]
    [InlineData("missing-manifest")]
    [InlineData("corrupt-manifest")]
    [InlineData("missing-library")]
    [InlineData("corrupt-library")]
    [InlineData("empty-library")]
    [InlineData("missing-license")]
    [InlineData("changed-commit")]
    [InlineData("changed-compiler")]
    [InlineData("changed-flags")]
    [InlineData("changed-abi")]
    [InlineData("changed-helper")]
    public void InstalledCacheRequiresMatchingProvenanceAndHashes(string mutation)
    {
        string root = Path.Combine(Path.GetTempPath(), "lumyte-dxc-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "lib"));
        try
        {
            string[] files = ["lib/libdxcompiler.so", "lib/libdxil.so", "LICENSE.TXT"];
            foreach (string file in files)
            {
                File.WriteAllText(Path.Combine(root, file), "fixture: " + file);
            }

            var inputs = new Dictionary<string, string>
            {
                ["commit"] = "pinned",
                ["compiler"] = "compiler hash",
                ["flags"] = "release",
                ["abi"] = "package closure",
                ["helper"] = "helper hash",
            };
            Invoke("Publish", root, inputs);
            Assert.True((bool)Invoke("Verify", root, Invoke("Fingerprint", inputs)));
            switch (mutation)
            {
                case "missing-manifest": File.Delete(Path.Combine(root, "provenance.json")); break;
                case "corrupt-manifest": File.WriteAllText(Path.Combine(root, "provenance.json"), "{broken"); break;
                case "missing-library": File.Delete(Path.Combine(root, files[0])); break;
                case "corrupt-library": File.WriteAllText(Path.Combine(root, files[1]), "wrong bytes"); break;
                case "empty-library": File.WriteAllText(Path.Combine(root, files[1]), string.Empty); break;
                case "missing-license": File.Delete(Path.Combine(root, files[2])); break;
                default:
                    if (mutation.StartsWith("changed-", StringComparison.Ordinal))
                    {
                        inputs[mutation[8..]] += " changed";
                    }

                    break;
            }

            Assert.Equal(mutation == "none", (bool)Invoke("Verify", root, Invoke("Fingerprint", inputs)));

            // Reinstall after every miss must recover, rather than accepting stale state.
            if (mutation != "none")
            {
                foreach (string file in files)
                {
                    File.WriteAllText(Path.Combine(root, file), "rebuilt: " + file);
                }

                Invoke("Publish", root, inputs);
                Assert.True((bool)Invoke("Verify", root, Invoke("Fingerprint", inputs)));
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Input order does not affect an exact cache identity.</summary>
    [Fact]
    public void InputFingerprintIsCanonical()
    {
        var first = new Dictionary<string, string> { ["a"] = "1", ["b"] = "2" };
        var second = new Dictionary<string, string> { ["b"] = "2", ["a"] = "1" };
        Assert.Equal(Invoke("Fingerprint", first), Invoke("Fingerprint", second));
    }

    /// <summary>A fresh installed-only restore skips every external tool; corruption takes the normal download path.</summary>
    /// <returns>The asynchronous assertion.</returns>
    [Fact]
    public async Task FreshSetupHitSkipsToolsAndCorruptionFallsBackAsync()
    {
        if (!OperatingSystem.IsLinux() || System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture != System.Runtime.InteropServices.Architecture.X64)
        {
            return;
        }

        using var fixture = new BuildFixture();
        string root = Path.Combine(fixture.Root, "environment");
        fixture.Environment["LUMYTE_ENV_ROOT"] = root;
        foreach (string variable in new[] { "CC", "CXX", "CFLAGS", "CXXFLAGS", "CPPFLAGS", "LDFLAGS", "CPATH", "C_INCLUDE_PATH", "CPLUS_INCLUDE_PATH", "LIBRARY_PATH", "LD_LIBRARY_PATH", "COMPILER_PATH", "GCC_EXEC_PREFIX", "SOURCE_DATE_EPOCH", "CMAKE_TOOLCHAIN_FILE", "CMAKE_PREFIX_PATH", "SDKROOT" })
        {
            fixture.Environment[variable] = null;
        }

        foreach (string relative in new[] { "build/Build.Dxc.cs", "build/DxcCache.cs", "tools/setup/dxc-compiler-options.cmake", "mise.toml", "mise.lock" })
        {
            string path = Path.Combine(fixture.Root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "fixture " + relative);
        }

        // Construct a fresh installed-only restore: no source/build trees or tools.
        var inputs = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["schema"] = "1",
            ["commit"] = "b106a961d09221b3c5bdb37be45b679257da08b8",
            ["architecture"] = "X64",
            ["osArchitecture"] = System.Runtime.InteropServices.RuntimeInformation.OSArchitecture.ToString(),
            ["os"] = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            ["sysroot"] = "absent",
        };
        foreach (string file in new[] { "build/Build.Dxc.cs", "build/DxcCache.cs", "tools/setup/dxc-compiler-options.cmake", "mise.toml", "mise.lock" })
        {
            inputs[file] = (string)Invoke("HashFile", Path.Combine(fixture.Root, file));
        }

        foreach (string file in new[] { "/etc/os-release", "/var/lib/dpkg/status" })
        {
            inputs[file] = (string)Invoke("HashFile", file);
        }

        foreach (string variable in fixture.Environment.Keys.Where(key => fixture.Environment[key] is null))
        {
            inputs["env:" + variable] = string.Empty;
        }

        string installed = Path.Combine(root, "dxc");
        Directory.CreateDirectory(Path.Combine(installed, "lib"));
        foreach (string file in new[] { "lib/libdxcompiler.so", "lib/libdxil.so", "LICENSE.TXT" })
        {
            File.WriteAllText(Path.Combine(installed, file), "fixture " + file);
        }

        Invoke("Publish", installed, inputs);
        BuildFixture.ProcessResult hit = await fixture.RunAsync("Setup", "--setup-tool", "dxc");
        Assert.Equal(0, hit.ExitCode);
        Assert.Contains("DXC verified cache hit:", hit.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Running:", hit.Output, StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(installed, "lib", "libdxcompiler.so"), "corrupt");
        BuildFixture.ProcessResult miss = await fixture.RunAsync("Setup", "--setup-tool", "dxc");
        Assert.NotEqual(0, miss.ExitCode); // No curl exists on this fixture's PATH.
        Assert.Contains("DXC cache miss:", miss.Output, StringComparison.Ordinal);
        Assert.Contains("Running: curl", miss.Output, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(installed, "provenance.json")));
        Directory.Delete(installed, recursive: true);
        BuildFixture.ProcessResult empty = await fixture.RunAsync("Setup", "--setup-tool", "dxc");
        Assert.NotEqual(0, empty.ExitCode);
        Assert.Contains("Running: curl", empty.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("DirectoryNotFoundException", empty.Output, StringComparison.Ordinal);
    }

    private static object Invoke(string name, params object[] arguments) =>
        Cache.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, arguments) ?? new object();
}
