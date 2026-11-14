using System.Text.Json.Nodes;
using Xunit;

namespace Lumyte.Build.Tests;

/// <summary>Checks Core category selection and argument boundaries without timing thresholds.</summary>
public sealed class CoreBenchmarkCatalogTests
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
            string[] all = CoreBenchmarkCatalog.SelectProjects(root, "*");
            Assert.Equal(2, all.Length);
            Assert.EndsWith("Lumyte.Mathematics.Benchmark.csproj", all[0], StringComparison.Ordinal);
            Assert.Single(CoreBenchmarkCatalog.SelectProjects(root, "Lumyte.Signals"));
            Assert.Single(CoreBenchmarkCatalog.SelectProjects(root, "Lumyte.Signals.Benchmark"));
            Assert.Throws<ArgumentException>(() => CoreBenchmarkCatalog.SelectProjects(root, "NoSuchProject"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Known modes are explicit rather than implicit fallback settings.</summary>
    /// <param name="mode">The requested mode.</param>
    [Theory]
    [InlineData("verify")]
    [InlineData("screen")]
    [InlineData("compare")]
    [InlineData("diagnose")]
    public void ModesAreExplicit(string mode) => Assert.Equal(mode, CoreBenchmarkCatalog.NormalizeMode(mode));

    /// <summary>Unknown modes cannot silently become a quick or empty success.</summary>
    [Fact]
    public void UnknownModeIsRejected() => Assert.Throws<ArgumentException>(() => CoreBenchmarkCatalog.NormalizeMode("screening-typo"));

    /// <summary>Paths and filters remain distinct process arguments.</summary>
    [Fact]
    public void ArgumentsPreserveUnicodePathsAndFamilyFilters()
    {
        string binary = "target with spaces/日本語.dll";
        string output = "output 日本語";
        Assert.Equal([binary, "--catalog"], CoreBenchmarkCatalog.Arguments(binary, "verify", "*", null, output, true));
        Assert.Equal([binary, "--mode", "verify"], CoreBenchmarkCatalog.Arguments(binary, "verify", "*", null, output, false));
        Assert.Equal(
            [binary, "--mode", "screen", "--filter", "*Updated*", "--artifacts", output, "--anyCategories", "CORE-SIG-02"],
            CoreBenchmarkCatalog.Arguments(binary, "screen", "*Updated*", "CORE-SIG-02", output, false));
        Assert.Throws<ArgumentException>(() => CoreBenchmarkCatalog.Arguments(binary, "compare", string.Empty, null, output, false));
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
        BuildFixture.ProcessResult result = await fixture.RunAsync("BenchmarkCore", "--benchmark-project", "NotPresent", "--dotnet-path", fixture.DotnetTool);
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

        BuildFixture.ProcessResult result = await fixture.RunAsync("BenchmarkCore", "--benchmark-project", "Lumyte.Demo", "--no-build", "--dotnet-path", fixture.DotnetTool);
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
        BuildFixture.ProcessResult initial = await fixture.RunAsync("BenchmarkCore", "--benchmark-project", "Lumyte.Demo", "--dotnet-path", fixture.DotnetTool);
        Assert.True(initial.ExitCode == 0, initial.Output + initial.Error);
        int commands = fixture.Records("dotnet").Length;
        Assert.Equal(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(output)))!, fixture.Records("dotnet")[^1].GetProperty("workingDirectory").GetString());
        Assert.Equal(Path.Combine(fixture.Root, "benchmarks", "Core", "Lumyte.Demo.Benchmark", "Lumyte.Demo.Benchmark.csproj"), fixture.Records("dotnet")[^1].GetProperty("environment").GetProperty("LUMYTE_BENCHMARK_PROJECT").GetString());
        File.WriteAllText(dependency, "replaced output");
        BuildFixture.ProcessResult changed = await fixture.RunAsync("BenchmarkCore", "--benchmark-project", "Lumyte.Demo", "--no-build", "--dotnet-path", fixture.DotnetTool);
        Assert.NotEqual(0, changed.ExitCode);
        Assert.Contains("No-build benchmark outputs", changed.Output + changed.Error, StringComparison.Ordinal);
        Assert.Equal(commands, fixture.Records("dotnet").Length);
    }

    /// <summary>Compiled categories select cases across unrelated project, file and type names.</summary>
    [Fact]
    public void MetadataSelectsAcrossProjectsWithoutNameInference()
    {
        JsonObject first = CoreBenchmarkCatalog.ParseCatalog("""
            {"SchemaVersion":1,"Assembly":{"Name":"Vector.Benchmark","Location":"/shapes/Fixtures.dll"},
             "Modes":["verify","screen","compare","diagnose"],"Cases":[
              {"Type":"Unrelated.Subject","Method":"Run","Categories":["Core","shared"],"Modes":["verify","screen"]},
              {"Type":"shared.NamedClass","Method":"Other","Categories":["Other"],"Modes":["screen"]}]}
            """);
        JsonObject second = CoreBenchmarkCatalog.ParseCatalog("""
            {"SchemaVersion":1,"Assembly":{"Name":"CompletelyDifferent.Benchmark","Location":"/elsewhere/Inputs.dll"},
             "Modes":["verify","screen","compare","diagnose"],"Cases":[
              {"Type":"Another.Namespace.Fixture","Method":"Work","Categories":["shared"],"Modes":["screen","diagnose"]}]}
            """);
        JsonArray[] selections = [CoreBenchmarkCatalog.SelectCases(first, "screen", "shared"), CoreBenchmarkCatalog.SelectCases(second, "screen", "shared")];
        Assert.All(selections, cases => Assert.Single(cases));
        CoreBenchmarkCatalog.RequireSelectedCases(selections, "shared", "screen");
        CoreBenchmarkCatalog.RequireSelectedCases([new JsonArray(), selections[1]], "shared", "screen");
        Assert.Equal("Unrelated.Subject", selections[0][0]!["Type"]!.GetValue<string>());
        Assert.Equal(2, first["Cases"]!.AsArray().Count);
        Assert.Empty(CoreBenchmarkCatalog.SelectCases(first, "screen", "Vector"));
        Assert.Empty(CoreBenchmarkCatalog.SelectCases(first, "screen", "Shared"));
        Assert.Empty(CoreBenchmarkCatalog.SelectCases(first, "screen", "Core,shared"));
        Assert.Throws<ArgumentException>(() => CoreBenchmarkCatalog.RequireSelectedCases(
            [CoreBenchmarkCatalog.SelectCases(first, "screen", "missing"), CoreBenchmarkCatalog.SelectCases(second, "screen", "missing")], "missing", "screen"));
    }

    /// <summary>Case modes, not category spelling, determine measurement eligibility.</summary>
    [Fact]
    public void DiagnosticCasesRequireEligibleMode()
    {
        JsonObject catalog = CoreBenchmarkCatalog.ParseCatalog("""
            {"SchemaVersion":1,"Modes":["verify","screen","compare","diagnose"],"Cases":[
              {"Type":"RegularLookingType","Categories":["diagnostic-only","subject"],"Modes":["verify","diagnose"]}]}
            """);
        Assert.Empty(CoreBenchmarkCatalog.SelectCases(catalog, "screen", "subject"));
        Assert.Empty(CoreBenchmarkCatalog.SelectCases(catalog, "compare", "subject"));
        Assert.Single(CoreBenchmarkCatalog.SelectCases(catalog, "verify", "subject"));
        Assert.Single(CoreBenchmarkCatalog.SelectCases(catalog, "diagnose", "subject"));
        Assert.Throws<ArgumentException>(() => CoreBenchmarkCatalog.RequireSelectedCases(
            [CoreBenchmarkCatalog.SelectCases(catalog, "screen", "subject")], "subject", "screen"));
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
    [InlineData("{\"SchemaVersion\":1,\"Modes\":[\"screen\"],\"Cases\":[{\"Categories\":[],\"Modes\":[]}]}")]
    [InlineData("{\"SchemaVersion\":1,\"Modes\":[\"screen\"],\"Cases\":[{\"Categories\":[],\"Modes\":[\"diagnose\"]}]}")]
    public void RejectsMalformedMetadata(string json) => Assert.Throws<ArgumentException>(() => CoreBenchmarkCatalog.ParseCatalog(json));

    private static void AddProject(string root, string name)
    {
        string directory = Path.Combine(root, "benchmarks", "Core", name + ".Benchmark");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, name + ".Benchmark.csproj"), "<Project />");
    }
}
