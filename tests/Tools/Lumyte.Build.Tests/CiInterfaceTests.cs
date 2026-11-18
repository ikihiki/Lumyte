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
            "--lane cpu", "--lane integration", "--lane gpu", "--pipeline-validation", "--platform browser", "--suite browser-contracts", "--suite aot",
            "--suite windowing", "--suite diagnostics-processes", "--suite diagnostics-ui",
            "--distribution-output diagnostic-gallery", "'--distribution-output', 'window-sample'", "'--distribution-output', 'packages'",
            "'--quality-scope', 'managed'", "--target Quality --configuration", "--restore-metadata",
            "VulkanProgramCapacitiesAsync", "MESA_SHADER_CACHE_DIR", "windows-11-arm", "ubuntu-24.04-arm",
            "LUMYTE_QUARANTINE_CONNECTED_PNG_TIMEOUT", "needs: [linux, windows, browser, aot]",
            "parley-native-", "parley-managed", "diagnostics-gallery", "retention-days: 30",
        })
        {
            Assert.Contains(contract, workflow, StringComparison.Ordinal);
        }

        Assert.True(workflow.IndexOf("VulkanProgramCapacitiesAsync", StringComparison.Ordinal) < workflow.IndexOf("--pipeline-validation", StringComparison.Ordinal));
    }

    /// <summary>DXC checkpoints and both balanced AOT groups remain required.</summary>
    [Fact]
    public void CountermeasureWorkflowPreservesTrustAndRequiredGroups()
    {
        string root = GetRepository();
        string workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "composition.yml"));
        Assert.DoesNotContain("--aot-group", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("--build-lane", workflow, StringComparison.Ordinal);
        Assert.Contains("--suite aot --configuration Release", workflow, StringComparison.Ordinal);
        Assert.Contains("needs: [linux, windows, browser, aot]", workflow, StringComparison.Ordinal);
        string setup = File.ReadAllText(Path.Combine(root, ".github", "actions", "setup-linux", "action.yml"));
        Assert.True(setup.IndexOf("Provision OS", StringComparison.Ordinal) < setup.IndexOf("actions/cache/restore@", StringComparison.Ordinal));
        Assert.True(setup.IndexOf("actions/cache/save@", StringComparison.Ordinal) < setup.IndexOf("Complete native setup", StringComparison.Ordinal));
        Assert.Contains("provenance.json", setup, StringComparison.Ordinal);
        Assert.DoesNotContain("version.txt", setup, StringComparison.Ordinal);
        Assert.DoesNotContain("refs/heads/main", setup, StringComparison.Ordinal);
        Assert.DoesNotContain("pull_request_target", workflow, StringComparison.Ordinal);
        Assert.Equal(4, Regex.Matches(workflow, @"uses: \./\.github/actions/cache-native").Count);
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
            Assert.Contains(Path.Combine("samples", "Lumyte.Graphics.DeviceCaps.Sample"), calls[1]);
            Assert.Contains("--no-build", calls[1]);
            Assert.Contains("--no-restore", calls[1]);
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

    /// <summary>Every browser scenario is mandatory and isolated, with a pre-test cache checkpoint.</summary>
    [Fact]
    public void BrowserMatrixAndCacheCheckpointsPreserveGates()
    {
        string workflow = File.ReadAllText(Path.Combine(GetRepository(), ".github", "workflows", "composition.yml"));
        string browser = workflow[workflow.IndexOf("  browser:", StringComparison.Ordinal)..workflow.IndexOf("  aot:", StringComparison.Ordinal)];
        Assert.DoesNotContain("matrix.case", browser, StringComparison.Ordinal);
        Assert.Contains("--lane integration --platform browser", browser, StringComparison.Ordinal);
        Assert.Contains("name: test-results-browser-ui-transitions", browser, StringComparison.Ordinal);
        Assert.DoesNotContain("strategy:", browser, StringComparison.Ordinal);
        Assert.DoesNotContain("--suite browser-contracts", browser, StringComparison.Ordinal);
        string linux = workflow[workflow.IndexOf("  linux:", StringComparison.Ordinal)..workflow.IndexOf("  windows:", StringComparison.Ordinal)];
        Assert.Contains("--suite browser-contracts", linux, StringComparison.Ordinal);
        Assert.Contains("timeout-minutes: 90", browser, StringComparison.Ordinal);
        Assert.True(browser.IndexOf("mode: save", StringComparison.Ordinal) < browser.IndexOf("Browser / platform API integration", StringComparison.Ordinal));
        string cache = File.ReadAllText(Path.Combine(GetRepository(), ".github", "actions", "cache-dependencies", "action.yml"));
        Assert.Contains("actions/cache/save@", cache, StringComparison.Ordinal);
        Assert.Contains("ci-apt/archives/*.deb", cache, StringComparison.Ordinal);
        Assert.Contains("tools/setup/ci-apt.sh", cache, StringComparison.Ordinal);
        string[] keys = cache.Split((char)10).Where(line => line.TrimStart().StartsWith("key:", StringComparison.Ordinal)).Select(line => line.Trim()).ToArray();
        Assert.Equal(2, keys.GroupBy(value => value).Count(group => group.Count() == 2));

        string gpu = linux;
        Assert.True(gpu.IndexOf("VulkanProgramCapacitiesAsync", StringComparison.Ordinal) < gpu.IndexOf("Cache ordinary GPU", StringComparison.Ordinal));
        Assert.Contains("git ls-files -z -- src tests native build tools .github", gpu, StringComparison.Ordinal);
        Assert.Contains("dpkg-query -W", gpu, StringComparison.Ordinal);
        Assert.Contains("steps.gpu-cache.outputs.driver", gpu, StringComparison.Ordinal);
        Assert.Contains("steps.gpu-cache.outputs.packages", gpu, StringComparison.Ordinal);
        Assert.Contains("steps.gpu-cache.outputs.source", gpu, StringComparison.Ordinal);
        Assert.DoesNotContain("restore-keys:", gpu, StringComparison.Ordinal);
    }

    /// <summary>One native preparation owns category execution; only aggregate budgets change.</summary>
    [Fact]
    public void NativeContextsShareOneFullBuildAndPrivateStageEnvironments()
    {
        string workflow = File.ReadAllText(Path.Combine(GetRepository(), ".github", "workflows", "composition.yml"));
        Assert.Equal(
            new[] { "linux", "windows", "browser", "aot", "package" },
            Regex.Matches(workflow[workflow.IndexOf("jobs:", StringComparison.Ordinal)..], @"^  ([a-z]+):$", RegexOptions.Multiline).Select(match => match.Groups[1].Value));
        foreach (string platform in new[] { "linux", "windows" })
        {
            int start = workflow.IndexOf("  " + platform + ":", StringComparison.Ordinal);
            int end = workflow.IndexOf(platform == "linux" ? "  windows:" : "  browser:", start, StringComparison.Ordinal);
            string native = workflow[start..end];
            Assert.Equal(2, Regex.Matches(native, @"arch: (?:x64|aarch64)").Count);
            Assert.Single(Regex.Matches(native, @"--target(?:', '|'? +)Compile"));
            Assert.Equal(2, Regex.Matches(native, @"uses: \./\.github/actions/cache-native").Count);
            Assert.DoesNotContain("--build-lane", native, StringComparison.Ordinal);
            Assert.DoesNotContain("continue-on-error", native, StringComparison.Ordinal);
            foreach (string step in native.Split("      - name:").Where(step => step.Contains("--target Test --lane", StringComparison.Ordinal) || step.Contains("'--lane',", StringComparison.Ordinal)))
            {
                Assert.Contains("--no-build", step, StringComparison.Ordinal);
                Assert.Contains("--no-restore", step, StringComparison.Ordinal);
            }

            string quality = native.Split("      - name:").Single(step => step.Contains("Quality /", StringComparison.Ordinal));
            Assert.Contains("if: matrix.arch == 'x64'", quality, StringComparison.Ordinal);
            Assert.Contains("if: always()", native, StringComparison.Ordinal);
        }

        string linux = workflow[workflow.IndexOf("  linux:", StringComparison.Ordinal)..workflow.IndexOf("  windows:", StringComparison.Ordinal)];
        foreach (string name in new[] { "Run CPU tests", "Native / cold-cache", "Fingerprint software GPU", "Cache ordinary GPU", "GPU backend" })
        {
            string step = linux.Split("      - name:").Single(step => step.TrimStart().StartsWith(name, StringComparison.Ordinal));
            Assert.Contains("if: matrix.arch == 'x64'", step, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("GITHUB_ENV", linux, StringComparison.Ordinal);
        Assert.True(linux.IndexOf("Run CPU tests", StringComparison.Ordinal) < linux.IndexOf("Run integration", StringComparison.Ordinal));
        Assert.True(linux.IndexOf("Run integration", StringComparison.Ordinal) < linux.IndexOf("Native / cold-cache", StringComparison.Ordinal));
        Assert.Contains("mktemp -d", linux, StringComparison.Ordinal);
        Assert.Contains("--test-concurrency 4", linux, StringComparison.Ordinal);
        Assert.Contains("Lumyte.Graphics.DeviceCaps.Sample", File.ReadAllText(Path.Combine(GetRepository(), "Lumyte.slnx")), StringComparison.Ordinal);
        string browser = workflow[workflow.IndexOf("  browser:", StringComparison.Ordinal)..workflow.IndexOf("  aot:", StringComparison.Ordinal)];
        Assert.Single(Regex.Matches(browser, "uses: ./.github/actions/setup-linux"));
        Assert.Single(Regex.Matches(browser, "uses: browser-actions/setup-chrome"));
        Assert.DoesNotContain("workload install", browser, StringComparison.Ordinal);
        Assert.DoesNotContain("--browser-case", browser, StringComparison.Ordinal);
        foreach (string name in new[] { "graphics", "text", "ui-application", "ui-navigation", "ui-pages", "ui-transitions", "audio" })
        {
            Assert.Contains("name: test-results-browser-" + name, browser, StringComparison.Ordinal);
        }
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
