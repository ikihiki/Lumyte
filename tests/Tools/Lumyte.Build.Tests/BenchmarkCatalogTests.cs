using System.Text.Json.Nodes;
using Xunit;

namespace Lumyte.Build.Tests;

/// <summary>Checks Core category selection and argument boundaries without timing thresholds.</summary>
public sealed class BenchmarkCatalogTests
{
    /// <summary>Only real target-specific project roots participate in selection.</summary>
    [Fact]
    public void DiscoversTargetProjectsAndRejectsMissingSelection()
    {
        string root = Path.Combine(Path.GetTempPath(), "Core catalog " + Guid.NewGuid().ToString("N"));
        try
        {
            AddProject(root, "Lumyte.Signals");
            AddProject(root, "Lumyte.Mathematics");
            Directory.CreateDirectory(Path.Combine(root, "benchmarks", "Core", "Shared"));
            Directory.CreateDirectory(Path.Combine(root, "benchmarks", "Core", "Missing.Benchmark"));
            string[] all = BenchmarkCatalog.SelectProjects(root, "*");
            Assert.Equal(2, all.Length);
            Assert.EndsWith("Lumyte.Mathematics.Benchmark.csproj", all[0], StringComparison.Ordinal);
            Assert.Single(BenchmarkCatalog.SelectProjects(root, "Lumyte.Signals"));
            Assert.Single(BenchmarkCatalog.SelectProjects(root, "Lumyte.Signals.Benchmark"));
            Assert.Throws<ArgumentException>(() => BenchmarkCatalog.SelectProjects(root, "NoSuchProject"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Each target resolves its logical project area without treating filenames as categories.</summary>
    [Fact]
    public void AnimationSelectionDoesNotIncludeCoreProjects()
    {
        string root = Path.Combine(Path.GetTempPath(), "Animation catalog " + Guid.NewGuid().ToString("N"));
        try
        {
            AddProject(root, "Lumyte.Core");
            string directory = Path.Combine(root, "benchmarks", "Animation", "Lumyte.Animation.Benchmark");
            Directory.CreateDirectory(directory);
            string project = Path.Combine(directory, "Lumyte.Animation.Benchmark.csproj");
            File.WriteAllText(project, "<Project />");
            Assert.Equal(new[] { project }, BenchmarkCatalog.SelectProjects(root, "*", "Animation"));
            Assert.Single(BenchmarkCatalog.SelectProjects(root, "*", "Core"));
            Assert.Throws<ArgumentException>(() => BenchmarkCatalog.SelectProjects(root, "*", "../Core"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Known modes are explicit rather than implicit fallback settings.</summary>
    /// <param name="mode">The requested mode.</param>
    [Theory]
    [InlineData("screen")]
    [InlineData("compare")]
    [InlineData("diagnose")]
    public void ModesAreExplicit(string mode) => Assert.Equal(mode, BenchmarkCatalog.NormalizeMode(mode));

    /// <summary>Unknown, removed and missing modes cannot silently become a timing run.</summary>
    /// <param name="mode">The invalid mode.</param>
    [Theory]
    [InlineData("screening-typo")]
    [InlineData("verify")]
    [InlineData("")]
    public void UnknownModeIsRejected(string mode) => Assert.Throws<ArgumentException>(() => BenchmarkCatalog.NormalizeMode(mode));

    /// <summary>Paths and filters remain distinct process arguments.</summary>
    [Fact]
    public void ArgumentsPreserveUnicodePathsAndFamilyFilters()
    {
        string binary = "target with spaces/日本語.dll";
        string output = "output 日本語";
        Assert.Equal([binary, "--catalog"], BenchmarkCatalog.Arguments(binary, null, "*", null, output, true));
        Assert.Throws<ArgumentException>(() => BenchmarkCatalog.Arguments(binary, "verify", "*", null, output, false));
        Assert.Throws<ArgumentNullException>(() => BenchmarkCatalog.Arguments(binary, null, "*", null, output, false));
        Assert.Equal(
            [binary, "--mode", "screen", "--filter", "*Updated*", "--artifacts", output, "--anyCategories", "Screening"],
            BenchmarkCatalog.Arguments(binary, "screen", "*Updated*", "Screening", output, false));
        Assert.Throws<ArgumentException>(() => BenchmarkCatalog.Arguments(binary, "compare", string.Empty, null, output, false));
    }

    /// <summary>Real Fallout validates mode before launching a project worker.</summary>
    /// <returns>The asynchronous entry-point assertion.</returns>
    [Fact]
    public async Task FalloutRejectsInvalidModeBeforeBuildAsync()
    {
        using var fixture = new BuildFixture();
        BuildFixture.ProcessResult result = await fixture.RunAsync("BenchmarkCore", "--benchmark-mode", "invalid", "--dotnet-path", fixture.DotnetTool);
        Assert.NotEqual(0, result.ExitCode);
        Assert.False(File.Exists(Path.Combine(fixture.Log, "dotnet.json")));
    }

    /// <summary>Unknown target projects fail rather than run a different product.</summary>
    /// <returns>The asynchronous selection assertion.</returns>
    [Fact]
    public async Task FalloutRejectsUnmatchedProjectAsync()
    {
        using var fixture = new BuildFixture();
        AddProject(fixture.Root, "Lumyte.Signals");
        BuildFixture.ProcessResult result = await fixture.RunAsync("BenchmarkCore", "--benchmark-mode", "screen", "--benchmark-project", "NotPresent", "--dotnet-path", fixture.DotnetTool);
        Assert.NotEqual(0, result.ExitCode);
        Assert.False(File.Exists(Path.Combine(fixture.Log, "dotnet.json")));
    }

    /// <summary>No-build cannot attach current source identity to unverified or stale binaries.</summary>
    /// <param name="stale">Whether an invalid build record exists.</param>
    /// <returns>The asynchronous provenance assertion.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NoBuildRejectsUnverifiedInputsAsync(bool stale)
    {
        using var fixture = new BuildFixture();
        AddProject(fixture.Root, "Lumyte.Demo");
        string source = Path.Combine(fixture.Root, "src", "Core");
        string shared = Path.Combine(fixture.Root, "benchmarks", "Core", "Shared");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(shared);
        File.WriteAllText(Path.Combine(source, "Product.cs"), "fixture source");
        File.WriteAllText(Path.Combine(shared, "BenchmarkEntry.cs"), "fixture shared source");
        string binary = Path.Combine(fixture.Root, "benchmarks", "Core", "Lumyte.Demo.Benchmark", "bin", "Release", "net10.0", "Lumyte.Demo.Benchmark.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(binary)!);
        File.WriteAllText(binary, "fixture binary");
        if (stale)
        {
            File.WriteAllText(binary + ".inputs.json", """{"inputs_sha256":"stale","binary_sha256":"stale"}""");
        }

        BuildFixture.ProcessResult result = await fixture.RunAsync("BenchmarkCore", "--benchmark-mode", "screen", "--benchmark-project", "Lumyte.Demo", "--no-build", "--dotnet-path", fixture.DotnetTool);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("No-build benchmark outputs", result.Output + result.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(fixture.Log, "dotnet.json")));
        Assert.True(File.Exists(Path.Combine(fixture.Output, "manifest.json")));
    }

    /// <summary>A copied product or runtime output cannot bypass valid entry-assembly provenance.</summary>
    /// <param name="relative">The mutated execution output.</param>
    /// <returns>The asynchronous dependency assertion.</returns>
    [Theory]
    [InlineData("Lumyte.Signals.dll")]
    [InlineData("Lumyte.Demo.Benchmark.deps.json")]
    [InlineData("Lumyte.Demo.Benchmark.runtimeconfig.json")]
    [InlineData("runtimes/linux-x64/native/shim.so")]
    public async Task NoBuildRejectsChangedExecutionDependencyAsync(string relative)
    {
        using var fixture = new BuildFixture();
        AddProject(fixture.Root, "Lumyte.Demo");
        string source = Path.Combine(fixture.Root, "src", "Core");
        string shared = Path.Combine(fixture.Root, "benchmarks", "Core", "Shared");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(shared);
        File.WriteAllText(Path.Combine(source, "Product.cs"), "fixture source");
        File.WriteAllText(Path.Combine(shared, "BenchmarkEntry.cs"), "fixture shared source");
        string output = Path.Combine(fixture.Root, "benchmarks", "Core", "Lumyte.Demo.Benchmark", "bin", "Release", "net10.0");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "Lumyte.Demo.Benchmark.dll"), "fixture entry");
        string dependency = Path.Combine(output, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(dependency)!);
        File.WriteAllText(dependency, "first output");
        BuildFixture.ProcessResult initial = await fixture.RunAsync("BenchmarkCore", "--benchmark-mode", "screen", "--benchmark-project", "Lumyte.Demo", "--dotnet-path", fixture.DotnetTool);
        Assert.True(initial.ExitCode == 0, initial.Output + initial.Error);
        int commands = fixture.Records("dotnet").Length;
        Assert.Equal(2, commands);
        Assert.Equal(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(output)))!, fixture.Records("dotnet")[^1].GetProperty("workingDirectory").GetString());
        Assert.Equal(Path.Combine(fixture.Root, "benchmarks", "Core", "Lumyte.Demo.Benchmark", "Lumyte.Demo.Benchmark.csproj"), fixture.Records("dotnet")[^1].GetProperty("environment").GetProperty("LUMYTE_BENCHMARK_PROJECT").GetString());
        File.WriteAllText(dependency, "replaced output");
        BuildFixture.ProcessResult changed = await fixture.RunAsync("BenchmarkCore", "--benchmark-mode", "screen", "--benchmark-project", "Lumyte.Demo", "--no-build", "--dotnet-path", fixture.DotnetTool);
        Assert.NotEqual(0, changed.ExitCode);
        Assert.Contains("No-build benchmark outputs", changed.Output + changed.Error, StringComparison.Ordinal);
        Assert.Equal(commands, fixture.Records("dotnet").Length);
    }

    /// <summary>Compiled categories select cases across unrelated project, file and type names.</summary>
    [Fact]
    public void MetadataSelectsAcrossProjectsWithoutNameInference()
    {
        JsonObject first = BenchmarkCatalog.ParseCatalog("""
            {"SchemaVersion":1,"Assembly":{"Name":"Vector.Benchmark","Location":"/shapes/Fixtures.dll"},
             "Modes":["screen","compare","diagnose"],"Cases":[
              {"Type":"Unrelated.Subject","Method":"Run","Categories":["Core","shared"],"Modes":["screen"]},
              {"Type":"shared.NamedClass","Method":"Other","Categories":["Other"],"Modes":["screen"]}]}
            """);
        JsonObject second = BenchmarkCatalog.ParseCatalog("""
            {"SchemaVersion":1,"Assembly":{"Name":"CompletelyDifferent.Benchmark","Location":"/elsewhere/Inputs.dll"},
             "Modes":["screen","compare","diagnose"],"Cases":[
              {"Type":"Another.Namespace.Fixture","Method":"Work","Categories":["shared"],"Modes":["screen","diagnose"]}]}
            """);
        JsonArray[] selections = [BenchmarkCatalog.SelectCases(first, "screen", "shared"), BenchmarkCatalog.SelectCases(second, "screen", "shared")];
        Assert.All(selections, cases => Assert.Single(cases));
        BenchmarkCatalog.RequireSelectedCases(selections, "shared", "screen");
        BenchmarkCatalog.RequireSelectedCases([new JsonArray(), selections[1]], "shared", "screen");
        Assert.Equal("Unrelated.Subject", selections[0][0]!["Type"]!.GetValue<string>());
        Assert.Equal(2, first["Cases"]!.AsArray().Count);
        Assert.Empty(BenchmarkCatalog.SelectCases(first, "screen", "Vector"));
        Assert.Empty(BenchmarkCatalog.SelectCases(first, "screen", "Shared"));
        Assert.Empty(BenchmarkCatalog.SelectCases(first, "screen", "Core,shared"));
        Assert.Throws<ArgumentException>(() => BenchmarkCatalog.RequireSelectedCases(
            [BenchmarkCatalog.SelectCases(first, "screen", "missing"), BenchmarkCatalog.SelectCases(second, "screen", "missing")], "missing", "screen"));
    }

    /// <summary>Case modes, not category spelling, determine measurement eligibility.</summary>
    [Fact]
    public void DiagnosticCasesRequireEligibleMode()
    {
        JsonObject catalog = BenchmarkCatalog.ParseCatalog("""
            {"SchemaVersion":1,"Modes":["screen","compare","diagnose"],"Cases":[
              {"Type":"RegularLookingType","Categories":["diagnostic-only","subject"],"Modes":["diagnose"]}]}
            """);
        Assert.Empty(BenchmarkCatalog.SelectCases(catalog, "screen", "subject"));
        Assert.Empty(BenchmarkCatalog.SelectCases(catalog, "compare", "subject"));
        Assert.Single(BenchmarkCatalog.SelectCases(catalog, null, "subject"));
        Assert.Single(BenchmarkCatalog.SelectCases(catalog, "diagnose", "subject"));
        Assert.Throws<ArgumentException>(() => BenchmarkCatalog.RequireSelectedCases(
            [BenchmarkCatalog.SelectCases(catalog, "screen", "subject")], "subject", "screen"));
    }

    /// <summary>Malformed compiled metadata cannot silently become an empty selection.</summary>
    /// <param name="json">An invalid catalog.</param>
    [Theory]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"SchemaVersion\":1,\"Cases\":[]}")]
    [InlineData("{\"SchemaVersion\":1,\"Modes\":[],\"Cases\":[]}")]
    [InlineData("{\"SchemaVersion\":1,\"Modes\":[\"screen\"],\"Cases\":{}}")]
    [InlineData("{\"SchemaVersion\":1,\"Modes\":\"screen\",\"Cases\":[]}")]
    [InlineData("{\"SchemaVersion\":1,\"Modes\":[\"unknown\"],\"Cases\":[]}")]
    [InlineData("{\"SchemaVersion\":1,\"Modes\":[\"screen\"],\"Cases\":[null]}")]
    [InlineData("{\"SchemaVersion\":1,\"Modes\":[\"screen\"],\"Cases\":[{\"Modes\":[\"screen\"]}]}")]
    [InlineData("{\"SchemaVersion\":1,\"Modes\":[\"screen\"],\"Cases\":[{\"Categories\":[12],\"Modes\":[\"screen\"]}]}")]
    [InlineData("{\"SchemaVersion\":1,\"Modes\":[\"screen\"],\"Cases\":[{\"Categories\":[],\"Modes\":[\"diagnose\"]}]}")]
    public void RejectsMalformedMetadata(string json) => Assert.Throws<ArgumentException>(() => BenchmarkCatalog.ParseCatalog(json));

    /// <summary>Listing includes cases with no measurement purpose without inventing a replacement mode.</summary>
    [Fact]
    public void UntaggedCasesRemainListableWithoutExecution()
    {
        JsonObject catalog = BenchmarkCatalog.ParseCatalog("""
            {"SchemaVersion":1,"Modes":["screen","compare","diagnose"],"Cases":[
              {"Type":"NoPurpose","Categories":["subject"],"Modes":[]}]}
            """);
        Assert.Single(BenchmarkCatalog.SelectCases(catalog, null, "subject"));
        Assert.Empty(BenchmarkCatalog.SelectCases(catalog, "screen", "subject"));
    }

    /// <summary>A missing mode cannot trigger a benchmark or a hidden correctness preflight.</summary>
    /// <returns>The asynchronous invocation assertion.</returns>
    [Fact]
    public async Task FalloutRequiresExplicitModeUnlessListingAsync()
    {
        using var fixture = new BuildFixture();
        AddProject(fixture.Root, "Lumyte.Demo");
        BuildFixture.ProcessResult missing = await fixture.RunAsync("BenchmarkCore", "--benchmark-project", "Lumyte.Demo", "--dotnet-path", fixture.DotnetTool);
        Assert.NotEqual(0, missing.ExitCode);
        Assert.False(File.Exists(Path.Combine(fixture.Log, "dotnet.json")));
        BuildFixture.ProcessResult removed = await fixture.RunAsync("BenchmarkCore", "--benchmark-mode", "verify", "--dotnet-path", fixture.DotnetTool);
        Assert.NotEqual(0, removed.ExitCode);
        Assert.False(File.Exists(Path.Combine(fixture.Log, "dotnet.json")));
    }

    /// <summary>The Animation target reuses the guarded runner and fingerprints Animation product inputs.</summary>
    /// <returns>The target and stale-source assertions.</returns>
    [Fact]
    public async Task AnimationTargetRejectsStaleProductInputsAsync()
    {
        using var fixture = new BuildFixture();
        string projectRoot = Path.Combine(fixture.Root, "benchmarks", "Animation", "Lumyte.Animation.Benchmark");
        Directory.CreateDirectory(projectRoot);
        File.WriteAllText(Path.Combine(projectRoot, "Lumyte.Animation.Benchmark.csproj"), "<Project />");
        string product = Path.Combine(fixture.Root, "src", "Animation");
        Directory.CreateDirectory(product);
        Directory.CreateDirectory(Path.Combine(fixture.Root, "src", "Core"));
        string shared = Path.Combine(fixture.Root, "benchmarks", "Core", "Shared");
        Directory.CreateDirectory(shared);
        File.WriteAllText(Path.Combine(shared, "BenchmarkEntry.cs"), "shared fixture");
        string source = Path.Combine(product, "Source.cs");
        File.WriteAllText(source, "original");
        string output = Path.Combine(projectRoot, "bin", "Release", "net10.0");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "Lumyte.Animation.Benchmark.dll"), "fixture output");
        BuildFixture.ProcessResult first = await fixture.RunAsync("BenchmarkAnimation", "--benchmark-mode", "screen", "--dotnet-path", fixture.DotnetTool);
        Assert.True(first.ExitCode == 0, first.Output + first.Error);
        int commands = fixture.Records("dotnet").Length;
        Assert.Equal(2, commands);
        File.WriteAllText(source, "changed animation product");
        BuildFixture.ProcessResult stale = await fixture.RunAsync("BenchmarkAnimation", "--benchmark-mode", "screen", "--no-build", "--dotnet-path", fixture.DotnetTool);
        Assert.NotEqual(0, stale.ExitCode);
        Assert.Contains("No-build benchmark outputs", stale.Output + stale.Error, StringComparison.Ordinal);
        Assert.Equal(commands, fixture.Records("dotnet").Length);
    }

    /// <summary>Input no-build provenance covers both its products and the Diagnostics consumer dependencies.</summary>
    /// <param name="changedArea">The product dependency changed after the build.</param>
    /// <returns>The stale-source rejection assertion.</returns>
    [Theory]
    [InlineData("Input")]
    [InlineData("Diagnostics")]
    public async Task InputTargetRejectsChangedDependenciesAsync(string changedArea)
    {
        using var fixture = new BuildFixture();
        string projectRoot = Path.Combine(fixture.Root, "benchmarks", "Input", "Lumyte.Input.Benchmark");
        Directory.CreateDirectory(projectRoot);
        File.WriteAllText(Path.Combine(projectRoot, "Lumyte.Input.Benchmark.csproj"), "<Project />");
        foreach (string area in new[] { "Core", "Input", "Diagnostics" })
        {
            string source = Path.Combine(fixture.Root, "src", area);
            Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "Product.cs"), "original");
        }

        string shared = Path.Combine(fixture.Root, "benchmarks", "Core", "Shared");
        Directory.CreateDirectory(shared);
        File.WriteAllText(Path.Combine(shared, "BenchmarkEntry.cs"), "shared fixture");
        string output = Path.Combine(projectRoot, "bin", "Release", "net10.0");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "Lumyte.Input.Benchmark.dll"), "fixture output");
        BuildFixture.ProcessResult first = await fixture.RunAsync("BenchmarkInput", "--benchmark-mode", "screen", "--dotnet-path", fixture.DotnetTool);
        Assert.True(first.ExitCode == 0, first.Output + first.Error);
        int commands = fixture.Records("dotnet").Length;
        Assert.Equal(2, commands);
        File.WriteAllText(Path.Combine(fixture.Root, "src", changedArea, "Product.cs"), "changed dependency");
        BuildFixture.ProcessResult stale = await fixture.RunAsync("BenchmarkInput", "--benchmark-mode", "screen", "--no-build", "--dotnet-path", fixture.DotnetTool);
        Assert.NotEqual(0, stale.ExitCode);
        Assert.Contains("No-build benchmark outputs", stale.Output + stale.Error, StringComparison.Ordinal);
        Assert.Equal(commands, fixture.Records("dotnet").Length);
    }

    private static void AddProject(string root, string name)
    {
        string directory = Path.Combine(root, "benchmarks", "Core", name + ".Benchmark");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name + ".Benchmark.csproj"), "<Project />");
    }
}
