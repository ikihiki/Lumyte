using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace Lumyte.Build.Tests;

/// <summary>Guards the small workflow API and the internal preparation boundaries.</summary>
public sealed class CiInterfaceTests
{
    /// <summary>The required workflow only composes four task-oriented entry points.</summary>
    [Fact]
    public void CompositionUsesOnlyTheFourPublicTasks()
    {
        string workflow = File.ReadAllText(Path.Combine(GetRepository(), ".github", "workflows", "composition.yml"));
        string[] targets = Regex.Matches(workflow, "--target(?:', '|'? +)([A-Za-z0-9]+)")
            .Select(match => match.Groups[1].Value).Distinct().Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(["Compile", "Distribution", "Quality", "Test"], targets);
        foreach (string contract in new[]
        {
            "--lane cpu", "--lane integration", "--lane gpu", "--pipeline-validation", "--suite browser", "--suite aot",
            "--suite windowing", "--suite diagnostics-processes", "--suite diagnostics-ui",
            "--distribution-output diagnostic-gallery", "'--distribution-output', 'window-sample'", "'--distribution-output', 'packages'",
            "'--quality-scope', 'managed'", "--target Quality --configuration", "--restore-metadata",
            "VulkanProgramCapacitiesAsync", "MESA_SHADER_CACHE_DIR", "windows-11-arm", "ubuntu-24.04-arm",
            "LUMYTE_QUARANTINE_CONNECTED_PNG_TIMEOUT", "needs: [cpu, gpu, linux, windows, browser, aot, format]",
            "parley-native-", "parley-managed", "diagnostics-gallery", "retention-days: 30",
        })
        {
            Assert.Contains(contract, workflow, StringComparison.Ordinal);
        }

        Assert.True(workflow.IndexOf("VulkanProgramCapacitiesAsync", StringComparison.Ordinal) < workflow.IndexOf("--pipeline-validation", StringComparison.Ordinal));
    }

    /// <summary>Native hooks never re-enter the solution task and keep isolated state and repository roots.</summary>
    [Fact]
    public void NativeHooksUseTheCommonBackendWithoutSolutionRecursion()
    {
        foreach ((string file, string backend) in new[]
        {
            ("ParleyNative.targets", "parley"), ("ParleyBrowser.targets", "parley-wasm"),
            ("WgpuNative.targets", "wgpu"), ("UsvgNative.targets", "usvg"), ("Direct3D12Native.targets", "direct3d12"),
        })
        {
            var document = XDocument.Load(Path.Combine(GetRepository(), "build", "Targets", file));
            string command = Assert.Single(document.Descendants("Exec").Select(element => element.Attribute("Command")!.Value), command => command.Contains("--target Native --native-backend " + backend + " ", StringComparison.Ordinal));
            Assert.Contains("--root ", command, StringComparison.Ordinal);
            Assert.Contains("--repository-root ", command, StringComparison.Ordinal);
            Assert.DoesNotContain("--target Compile", command, StringComparison.Ordinal);
        }
    }

    /// <summary>Metadata preparation is explicit and must complete before packaging downloaded binaries.</summary>
    /// <param name="failRestore">Whether metadata restoration fails.</param>
    /// <returns>The completed process assertion.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Execution", "Integration")]
    public async Task DownloadedPackageMetadataPrecedesPackAsync(bool failRestore)
    {
        using var fixture = new BuildFixture();
        if (failRestore)
        {
            fixture.Environment["LUMYTE_TEST_DOTNET_FAIL_COMMAND"] = "restore";
            fixture.Environment["LUMYTE_TEST_DOTNET_EXIT"] = "23";
        }

        BuildFixture.ProcessResult result = await fixture.RunAsync("Distribution", "--distribution-output", "packages", "--restore-metadata", "--no-build", "--project", fixture.Project, "--dotnet-path", fixture.DotnetTool);
        Assert.Equal(!failRestore, result.ExitCode == 0);
        string[] commands = fixture.Records("dotnet").Select(record => record.GetProperty("arguments")[0].GetString()!).ToArray();
        Assert.Equal(failRestore ? ["restore"] : new[] { "restore", "pack" }, commands);
    }

    /// <summary>Removed implementation nodes are genuinely absent rather than hidden aliases.</summary>
    /// <param name="target">Former target name.</param>
    /// <returns>The completed no-side-effects assertion.</returns>
    [Theory]
    [InlineData("Restore")]
    [InlineData("Pack")]
    [InlineData("Samples")]
    [InlineData("Ci")]
    [InlineData("PrepareTest")]
    [InlineData("ValidateTest")]
    [InlineData("PreparePublish")]
    [InlineData("PipelineBenchmark")]
    [InlineData("DiagnosticGallery")]
    [InlineData("PublishWindowSample")]
    [InlineData("CheckFormat")]
    [InlineData("CheckNativeFormat")]
    [InlineData("CheckMarkdown")]
    [InlineData("FormatNative")]
    [InlineData("FormatMarkdown")]
    [InlineData("ParleyNative")]
    [InlineData("ParleyWasm")]
    [InlineData("UsvgNative")]
    [InlineData("WgpuNative")]
    [InlineData("Direct3D12Native")]
    [InlineData("SetupWasm")]
    [InlineData("SetupParleyNative")]
    [InlineData("SetupParleyWasm")]
    [InlineData("SetupDxc")]
    [InlineData("ConfigureLavapipe")]
    [InlineData("VerifySdk")]
    [InlineData("NativeSmoke")]
    [InlineData("VerifyEnvironment")]
    [InlineData("PrepareWasmTools")]
    [Trait("Execution", "Integration")]
    public async Task RemovedTargetsCannotExecuteAsync(string target)
    {
        using var fixture = new BuildFixture();
        BuildFixture.ProcessResult result = await fixture.RunAsync(target, "--dotnet-path", fixture.DotnetTool);
        Assert.NotEqual(0, result.ExitCode);
        Assert.False(Directory.Exists(fixture.Log));
    }

    /// <summary>All quality checks execute and failures stop dependent work, without dropping native or Markdown checks.</summary>
    /// <param name="failure">Optional failing worker.</param>
    /// <returns>The completed quality assertion.</returns>
    [Theory]
    [InlineData(null)]
    [InlineData("managed")]
    [InlineData("clang-format")]
    [InlineData("node")]
    [Trait("Execution", "Integration")]
    public async Task QualityRetainsAllChecksAndPropagatesFailuresAsync(string? failure)
    {
        using var fixture = new BuildFixture();
        fixture.Environment["LUMYTE_TEST_QUALITY_TOOLS"] = "1";
        fixture.Environment["LUMYTE_TEST_QUALITY_FAIL_TOOL"] = failure;
        if (failure == "managed")
        {
            fixture.Environment["LUMYTE_TEST_DOTNET_EXIT"] = "23";
        }

        foreach (string tool in new[] { "git", "mise", "clang-format" })
        {
            File.Copy(fixture.DotnetTool, Path.Combine(fixture.Tools, tool + (OperatingSystem.IsWindows() ? ".exe" : string.Empty)));
        }

        File.WriteAllText(Path.Combine(fixture.Root, "native.cpp"), "int value;\n");
        File.WriteAllText(Path.Combine(fixture.Root, "markdownlint.mjs"), "// fixture\n");
        BuildFixture.ProcessResult result = await fixture.RunAsync("Quality", "--project", fixture.Project, "--dotnet-path", fixture.DotnetTool, "--node", fixture.Node);
        Assert.Equal(failure is null, result.ExitCode == 0);
        Assert.Single(fixture.Records("dotnet"));
        Assert.Equal(failure != "managed", File.Exists(Path.Combine(fixture.Log, "clang-format.json")));
        Assert.Equal(failure is null or "node", File.Exists(Path.Combine(fixture.Log, "node.json")));
        if (failure is null)
        {
            string native = fixture.Record("clang-format").GetProperty("arguments").ToString();
            Assert.Contains("--dry-run", native, StringComparison.Ordinal);
            Assert.Contains("--Werror", native, StringComparison.Ordinal);
            Assert.DoesNotContain("--fix", fixture.Record("node").GetProperty("arguments").ToString(), StringComparison.Ordinal);
        }
    }

    /// <summary>GPU validation output follows the classified SDK test, never a failing gate.</summary>
    /// <param name="failTest">Whether the managed GPU gate fails.</param>
    /// <returns>The completed output ordering assertion.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Execution", "Integration")]
    public async Task PipelineOutputFollowsGpuGateAsync(bool failTest)
    {
        using var fixture = new BuildFixture();
        File.WriteAllText(fixture.Project, "<Project><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>");
        File.WriteAllText(fixture.Solution, new XDocument(new XElement("Solution", new XElement("Project", new XAttribute("Path", fixture.Project)))).ToString());
        Directory.CreateDirectory(Path.Combine(fixture.Root, "build"));
        File.WriteAllText(Path.Combine(fixture.Root, "build", "TestLanes.json"), System.Text.Json.JsonSerializer.Serialize(new[]
        {
            new { Project = Path.GetFileName(fixture.Project), Default = "Cpu", IntegrationClasses = Array.Empty<string>(), Lanes = new[] { "Cpu", "Gpu" } },
        }));
        if (failTest)
        {
            fixture.Environment["LUMYTE_TEST_DOTNET_FAIL_COMMAND"] = "test";
            fixture.Environment["LUMYTE_TEST_DOTNET_EXIT"] = "23";
        }

        BuildFixture.ProcessResult result = await fixture.RunAsync("Test", "--lane", "gpu", "--pipeline-validation", "--no-build", "--dotnet-path", fixture.DotnetTool);
        Assert.Equal(!failTest, result.ExitCode == 0);
        string[][] calls = fixture.Records("dotnet").Select(record => record.GetProperty("arguments").EnumerateArray().Select(value => value.GetString()!).ToArray()).ToArray();
        Assert.Equal(failTest ? ["test"] : new[] { "test", "run" }, calls.Select(call => call[0]));
        Assert.EndsWith("gpu.slnf", calls[0][1], StringComparison.Ordinal);
        if (!failTest)
        {
            Assert.Contains("samples/Lumyte.Graphics.DeviceCaps.Sample", calls[1]);
            Assert.Equal(new[] { "vulkan", "pipeline-benchmark" }, calls[1][^2..]);
            Assert.True(File.Exists(Path.Combine(fixture.Root, "artifacts", "test-results", "vulkan-pipeline-benchmark.jsonl")));
        }
    }

    /// <summary>Gallery output includes all retained validation workers without restoring or compiling the solution.</summary>
    /// <returns>The completed gallery command assertion.</returns>
    [Fact]
    [Trait("Execution", "Integration")]
    public async Task GalleryRetainsGeneratorAssemblerAndCollectorChecksAsync()
    {
        using var fixture = new BuildFixture();
        fixture.Environment["LUMYTE_TEST_QUALITY_TOOLS"] = "1";
        BuildFixture.ProcessResult result = await fixture.RunAsync("Distribution", "--distribution-output", "diagnostic-gallery", "--node", fixture.Node, "--python", fixture.Python, "--dotnet-path", fixture.DotnetTool);
        Assert.Equal(0, result.ExitCode);
        Assert.False(File.Exists(Path.Combine(fixture.Log, "dotnet.jsonl")));
        string[] workers = fixture.Records("node").Select(record => record.GetProperty("arguments")[0].GetString()!).ToArray();
        Assert.Equal(new[] { "tools/diagnostics/generate-gallery.test.mjs", "tools/diagnostics/assemble-gallery-site.test.mjs", "tools/diagnostics/generate-gallery.mjs" }, workers);
        Assert.Contains(fixture.Records("python"), record => record.GetProperty("arguments").ToString().Contains("test_collect_gallery_artifacts.py", StringComparison.Ordinal));
    }

    private static string GetRepository()
    {
        string directory = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(directory, "Lumyte.slnx")))
        {
            directory = Path.GetDirectoryName(directory.TrimEnd(Path.DirectorySeparatorChar)) ?? throw new DirectoryNotFoundException("Repository root not found.");
        }

        return directory;
    }
}
