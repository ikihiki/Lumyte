using System.Formats.Tar;
using System.Text.Json.Nodes;
using Xunit;

namespace Lumyte.Build.Tests;

/// <summary>Checks the CI artifact contract through the real build runner.</summary>
public sealed class CiBuildTests
{
    /// <summary>Archives retain project layout and executable modes without copying caches, tools or source files.</summary>
    /// <returns>The asynchronous archive assertion.</returns>
    [Fact]
    public async Task ArchiveRoundTripIncludesOnlyBuildOutputsAsync()
    {
        using var fixture = new BuildFixture();
        PrepareOutputs(fixture);
        string[] included =
        [
            "tests/Tools/Example/FakeTool/bin/Release/net10.0/FakeTool",
            "src/Example/obj/project.assets.json",
            "samples/Example/bin/Release/net10.0/Example.dll",
            "benchmarks/Example/obj/Release/net10.0/Example.dll",
            "artifacts/direct3d12/win-x64/1.0/Release/lumyte_graphics_d3d12.dll",
        ];
        string[] excluded =
        [
            "src/Example/Source.cs",
            "artifacts/text-parley/native/cargo/release/intermediate.o",
            "artifacts/build/tools/python/bin/python",
            "artifacts/dev-env/nuget-packages/package/package.nupkg",
            "artifacts/direct3d12/win-x64/1.0/Release/CMakeCache.txt",
            "artifacts/test-results/results.trx",
            "artifacts/nuget/local-package.nupkg",
        ];
        foreach (string path in included.Concat(excluded))
        {
            WriteFixtureFile(fixture, path);
        }

        string executable = Path.Combine(fixture.Root, included[0]);
        UnixFileMode mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute;
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(executable, mode);
        }

        AssertSuccess(await fixture.RunAsync("ArchiveCiBuild"));
        string archive = Path.Combine(fixture.Root, "artifacts/ci/build.tar");
        string extracted = Path.Combine(fixture.Root, "extracted 日本語");
        Directory.CreateDirectory(extracted);
        TarFile.ExtractToDirectory(archive, extracted, overwriteFiles: false);
        foreach (string path in included)
        {
            Assert.Equal(path, File.ReadAllText(Path.Combine(extracted, path)));
        }

        foreach (string path in excluded)
        {
            Assert.False(File.Exists(Path.Combine(extracted, path)), "Unexpected artifact entry: " + path);
        }

        Assert.True(File.Exists(Path.Combine(extracted, "artifacts/ci/build-manifest.json")));
        Assert.True(File.Exists(Path.Combine(extracted, "artifacts/text-parley/native", BuildFixture.NativeFileName)));
        Assert.True(File.Exists(Path.Combine(extracted, "artifacts/text-parley/package/runtimes/test/native", BuildFixture.NativeFileName)));
        Assert.True(File.Exists(Path.Combine(extracted, "build/bin/Release/net10.0/_build.dll")));
        if (!OperatingSystem.IsWindows())
        {
            using FileStream stream = File.OpenRead(archive);
            using var reader = new TarReader(stream);
            TarEntry? entry;
            do
            {
                entry = reader.GetNextEntry();
                Assert.NotNull(entry);
            }
            while (entry.Name != included[0]);

            Assert.Equal(mode, entry.Mode);

            // Extraction applies the consumer's umask, but apphosts must stay executable.
            Assert.True(File.GetUnixFileMode(Path.Combine(extracted, included[0])).HasFlag(UnixFileMode.UserExecute));
        }

        AssertSuccess(await fixture.RunAsync("VerifyCiBuild"));
        Assert.False(Directory.Exists(fixture.Log));
    }

    /// <summary>A producer's absolute paths, runtime and configuration must match the consuming runner.</summary>
    /// <param name="property">The manifest field changed to emulate a mismatched producer.</param>
    /// <param name="value">An incompatible producer value.</param>
    /// <returns>The asynchronous compatibility assertion.</returns>
    [Theory]
    [InlineData("RepositoryRoot", "/different/checkout")]
    [InlineData("RuntimeIdentifier", "different-runtime")]
    [InlineData("Configuration", "Debug")]
    public async Task VerifyRejectsIncompatibleManifestAsync(string property, string value)
    {
        using var fixture = new BuildFixture();
        PrepareOutputs(fixture);
        AssertSuccess(await fixture.RunAsync("ArchiveCiBuild"));
        string path = Path.Combine(fixture.Root, "artifacts/ci/build-manifest.json");
        JsonNode manifest = JsonNode.Parse(File.ReadAllText(path))!;
        manifest[property] = value;
        File.WriteAllText(path, manifest.ToJsonString());

        BuildFixture.ProcessResult result = await fixture.RunAsync("VerifyCiBuild");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("same runner label", result.Output + result.Error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(fixture.Log));
    }

    /// <summary>The consumer cannot request another build configuration for an existing artifact.</summary>
    /// <returns>The asynchronous configuration assertion.</returns>
    [Fact]
    public async Task VerifyRejectsAnotherRequestedConfigurationAsync()
    {
        using var fixture = new BuildFixture();
        PrepareOutputs(fixture);
        AssertSuccess(await fixture.RunAsync("ArchiveCiBuild"));
        BuildFixture.ProcessResult result = await fixture.RunAsync("VerifyCiBuild", "--configuration", "Debug");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("same runner label", result.Output + result.Error, StringComparison.Ordinal);
    }

    /// <summary>An empty checkout cannot publish an apparently successful build artifact.</summary>
    /// <returns>The asynchronous missing-build assertion.</returns>
    [Fact]
    public async Task ArchiveRejectsMissingCompiledOutputsAsync()
    {
        using var fixture = new BuildFixture();
        BuildFixture.ProcessResult result = await fixture.RunAsync("ArchiveCiBuild");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("CI build outputs are incomplete", result.Output + result.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(fixture.Root, "artifacts/ci/build.tar")));
        Assert.False(Directory.Exists(fixture.Log));
    }

    /// <summary>A matching manifest does not hide missing files in a downloaded artifact.</summary>
    /// <returns>The asynchronous incomplete-artifact assertion.</returns>
    [Fact]
    public async Task VerifyRejectsIncompleteBuildOutputsAsync()
    {
        using var fixture = new BuildFixture();
        PrepareOutputs(fixture);
        AssertSuccess(await fixture.RunAsync("ArchiveCiBuild"));
        File.Delete(Path.Combine(fixture.Root, "tests/Core/Lumyte.Signals.Tests/bin/Release/net10.0/Lumyte.Signals.Tests.dll"));
        BuildFixture.ProcessResult result = await fixture.RunAsync("VerifyCiBuild");
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("CI build outputs are incomplete", result.Output + result.Error, StringComparison.Ordinal);
    }

    private static void PrepareOutputs(BuildFixture fixture)
    {
        foreach (string root in new[] { "build", "src", "tests", "samples", "benchmarks" })
        {
            Directory.CreateDirectory(Path.Combine(fixture.Root, root));
        }

        foreach (string path in new[]
        {
            "build/bin/Release/net10.0/_build.dll",
            "src/Graphics/Lumyte.Graphics.Text.Parley/bin/Release/net10.0/Lumyte.Graphics.Text.Parley.dll",
            "tests/Core/Lumyte.Signals.Tests/bin/Release/net10.0/Lumyte.Signals.Tests.dll",
            $"artifacts/text-parley/native/{BuildFixture.NativeFileName}",
            "artifacts/text-parley/native/RUST-STANDARD-LIBRARY-NOTICES.html",
            $"artifacts/text-parley/package/runtimes/test/native/{BuildFixture.NativeFileName}",
        })
        {
            WriteFixtureFile(fixture, path);
        }
    }

    private static void WriteFixtureFile(BuildFixture fixture, string path)
    {
        string destination = Path.Combine(fixture.Root, path);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllText(destination, path);
    }

    private static void AssertSuccess(BuildFixture.ProcessResult result)
        => Assert.True(result.ExitCode == 0, result.Output + "\n" + result.Error);
}
