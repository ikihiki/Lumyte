using System.Text.Json;
using Xunit;

namespace Lumyte.Build.Tests;

/// <summary>Producer/consumer contracts for independently published test artifacts.</summary>
public sealed class PrebuiltPublishTests
{
    /// <summary>The AOT producer publishes all projects strictly without executing samples.</summary>
    /// <returns>The completed contract check.</returns>
    [Fact]
    [Trait("Execution", "Integration")]
    public async Task AotProducerPublishesFourteenStrictApplicationsAsync()
    {
        using var fixture = new BuildFixture();
        fixture.Environment["LUMYTE_TEST_STAGE_AOT"] = "1";
        BuildFixture.ProcessResult result = await fixture.RunAsync("Compile", "--build-kind", "aot", "--dotnet-path", fixture.DotnetTool);
        Assert.True(result.ExitCode == 0, result.Output + result.Error);
        JsonElement[] records = fixture.Records("dotnet");
        Assert.Equal(14, records.Length);
        Assert.All(records, record =>
        {
            string[] arguments = Arguments(record);
            Assert.Equal("publish", arguments[0]);
            Assert.Contains("-p:PublishAot=true", arguments);
            Assert.Contains("-p:ILLinkTreatWarningsAsErrors=true", arguments);
            Assert.Contains("-p:IlcTreatWarningsAsErrors=true", arguments);
        });
        Assert.Single(Directory.GetFiles(fixture.Log, "*.jsonl"));
    }

    /// <summary>Seven API cases retain both audio applications and publish no duplicate project.</summary>
    /// <returns>The completed contract check.</returns>
    [Fact]
    [Trait("Execution", "Integration")]
    public async Task BrowserProducerPreservesEightApplicationsAsync()
    {
        using var fixture = new BuildFixture();
        StageBrowser(fixture);
        File.WriteAllText(Path.Combine(fixture.Root, "global.json"), """{"sdk":{"version":"10.0.403"}}""");
        BuildFixture.ProcessResult result = await fixture.RunAsync("Compile", "--build-kind", "browser", "--dotnet-path", fixture.DotnetTool);
        Assert.True(result.ExitCode == 0, result.Output + result.Error);
        string[][] records = fixture.Records("dotnet").Select(Arguments).ToArray();
        Assert.Equal("workload", records[0][0]);
        string[][] publishes = records.Where(arguments => arguments[0] == "publish").ToArray();
        Assert.Equal(8, publishes.Length);
        Assert.Equal(8, publishes.Select(arguments => arguments[1]).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(2, publishes.Count(arguments => arguments[1].Contains("Audio", StringComparison.Ordinal)));
    }

    /// <summary>Browser consumers validate artifacts and never invoke the SDK.</summary>
    /// <param name="missing">Whether the downloaded artifact is missing.</param>
    /// <returns>The completed contract check.</returns>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Execution", "Integration")]
    public async Task BrowserConsumerNeverPublishesAsync(bool missing)
    {
        using var fixture = new BuildFixture();
        if (!missing)
        {
            StageBrowser(fixture);
        }

        fixture.Environment["LUMYTE_TEST_QUALITY_TOOLS"] = "1";
        BuildFixture.ProcessResult result = await fixture.RunAsync("Test", "--suite", "browser", "--browser-case", "graphics", "--no-build", "--dotnet-path", fixture.DotnetTool, "--node", fixture.Node);
        Assert.Equal(missing, result.ExitCode != 0);
        Assert.False(File.Exists(Path.Combine(fixture.Log, "dotnet.jsonl")));
        if (missing)
        {
            Assert.Contains("Compile --build-kind browser", result.Output + result.Error, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(fixture.Log, "node.jsonl")));
        }
    }

    /// <summary>Missing prebuilt AOT outputs fail before any SDK or executable invocation.</summary>
    /// <returns>The completed contract check.</returns>
    [Fact]
    [Trait("Execution", "Integration")]
    public async Task AotConsumerRejectsMissingArtifactWithoutPublishingAsync()
    {
        using var fixture = new BuildFixture();
        BuildFixture.ProcessResult result = await fixture.RunAsync("Test", "--suite", "aot", "--aot-group", "balanced-1", "--no-build", "--dotnet-path", fixture.DotnetTool);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Compile --build-kind aot", result.Output + result.Error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(fixture.Log));
    }

    /// <summary>A producer's exact fourteen executable outputs feed both groups without republishing.</summary>
    /// <returns>The completed consumer assertions.</returns>
    [Fact]
    [Trait("Execution", "Integration")]
    public async Task AotProducedExecutablesRunBothGroupsWithoutPublishingAsync()
    {
        using var fixture = new BuildFixture();
        fixture.Environment["LUMYTE_TEST_STAGE_AOT"] = "1";
        BuildFixture.ProcessResult producer = await fixture.RunAsync("Compile", "--build-kind", "aot", "--dotnet-path", fixture.DotnetTool);
        Assert.True(producer.ExitCode == 0, producer.Output + producer.Error);
        File.Delete(Path.Combine(fixture.Log, "dotnet.jsonl"));
        foreach (string group in new[] { "balanced-1", "balanced-2" })
        {
            BuildFixture.ProcessResult consumer = await fixture.RunAsync("Test", "--suite", "aot", "--aot-group", group, "--no-build", "--no-restore", "--dotnet-path", fixture.DotnetTool);
            Assert.True(consumer.ExitCode == 0, consumer.Output + consumer.Error);
        }

        Assert.False(File.Exists(Path.Combine(fixture.Log, "dotnet.jsonl")));
        Assert.Equal(14, Directory.GetFiles(fixture.Log, "*.jsonl").Length);
        Assert.Equal(8, fixture.Records("Lumyte.Resources.Audio.IntegrationTests").Length);
        Assert.Equal(4, fixture.Records("Lumyte.Execution.Sample").Length);
    }

    private static string[] Arguments(JsonElement record) => record.GetProperty("arguments").EnumerateArray().Select(value => value.GetString()!).ToArray();

    private static void StageBrowser(BuildFixture fixture)
    {
        foreach (string directory in new[] { "browser/graphics", "browser/text", "browser/ui-application", "browser/ui-navigation", "browser/ui-pages", "browser/ui-transitions", "audio-execution-browser", "audio-browser" })
        {
            string root = Path.Combine(fixture.Root, "artifacts", directory, "wwwroot");
            Directory.CreateDirectory(Path.Combine(root, "_framework"));
            File.WriteAllText(Path.Combine(root, "index.html"), "fixture");
            File.WriteAllText(Path.Combine(root, "_framework", "fixture.wasm"), "fixture");
        }
    }
}
