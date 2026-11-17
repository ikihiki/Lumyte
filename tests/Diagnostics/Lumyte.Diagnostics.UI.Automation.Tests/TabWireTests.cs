using System.Text.Json;
using Lumyte.Diagnostics.UI.Automation.Client;
using Lumyte.Diagnostics.UI.Automation.Contracts;
using Lumyte.UI.Controls;
using Xunit;
using static Lumyte.UI.Compose;
using static Lumyte.UI.Controls.Compose;

namespace Lumyte.Diagnostics.UI.Automation.Tests;

/// <summary>Freezes old shapes while exposing retained panels through schema eight.</summary>
public sealed class TabWireTests
{
    /// <summary>New roles and actions require eight; unknown fields remain closed.</summary>
    [Fact]
    public void ExpansionSchemaIsClosedAndVersioned()
    {
        var plan = new UiRemotePlan(8, "expand", [new("role", Role: "expander")]);
        Assert.True(UiRemoteSchema.IsAction(UiRemoteSchema.ParsePlan(UiRemoteSchema.SerializePlan(plan))));
        Assert.Throws<JsonException>(() => UiRemoteSchema.SerializePlan(plan with { SchemaVersion = 7 }));
        Assert.Throws<JsonException>(() => UiRemoteSchema.ParsePlan("{\"schemaVersion\":8,\"kind\":\"expand\",\"locator\":[{\"kind\":\"role\",\"role\":\"expander\"}],\"isExpanded\":true}"));
    }

    /// <summary>Actual runtime input is idempotent, leased and forbidden to observation pages.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task RemoteExpansionUsesOrdinaryInputAsync()
    {
        int changes = 0;
        using var handler = new LoopbackHandler(configureSurface: surface => surface.Rebuild(Expander(testId: "details", onChanged: _ => changes++)[Expander.Content()[SizedBox(width: 100, height: 60)]]));
        using var http = new HttpClient(handler) { BaseAddress = new("http://diagnostics/") };
        await using var client = new UiAutomationDiagnosticsClient(http, await handler.Ready);
        UiRemoteTarget target = Assert.Single(await client.ListTargetsAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.Page(target.Id).GetByRole("expander").ExpandAsync());
        await using UiRemoteControl control = await client.AcquireControlAsync(target.Id);
        await control.Page.GetByRole("expander").ExpandAsync();
        await control.Page.GetByRole("expander").ExpandAsync();
        await control.Page.GetByRole("expander").CollapseAsync();
        Assert.Equal(2, changes);
    }

    /// <summary>Every old strict reader retains its exact field set and numeric metadata.</summary>
    /// <param name="version">The requested wire version.</param>
    /// <returns>The asynchronous check.</returns>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    public async Task ExpandedMetadataDoesNotChangeOldShapesAsync(int version)
    {
        using var handler = new LoopbackHandler(configureSurface: surface => surface.Rebuild(TabView(selectedKey: "a")[TabItem(key: "a")[TabItem.Content()[Expander(isExpanded: true, testId: "details")[Expander.Content()[Slider(value: 0.25, testId: "numeric")]]]]]));
        using var http = new HttpClient(handler) { BaseAddress = new("http://diagnostics/") };
        await using var client = new UiAutomationDiagnosticsClient(http, await handler.Ready);
        UiRemoteTarget target = Assert.Single(await client.ListTargetsAsync());
        UiRemoteJob job = await client.Page(target.Id).StartObservationAsync(new(version, "snapshot", []));
        UiRemoteJobStatus status = await job.WaitAsync();
        byte[] bytes = await job.DownloadArtifactAsync(status.ResultArtifactId!.Value);
        if (version < 4)
        {
            Assert.NotNull(JsonSerializer.Deserialize(bytes, OldWireContext.Default.OldSnapshot));
        }
        else if (version < 6)
        {
            Assert.NotNull(JsonSerializer.Deserialize(bytes, FrozenV4Context.Default.FrozenV4Snapshot));
        }
        else if (version < 8)
        {
            Assert.NotNull(JsonSerializer.Deserialize(bytes, FrozenV6Context.Default.FrozenV6Snapshot));
        }

        using var raw = JsonDocument.Parse(bytes);
        foreach (JsonElement node in raw.RootElement.GetProperty("nodes").EnumerateArray())
        {
            Assert.Equal(version < 4 ? 18 : version < 6 ? 23 : version < 8 ? 24 : 25, node.EnumerateObject().Count());
            if (version < 8)
            {
                Assert.DoesNotContain(node.GetProperty("role").GetString(), new[] { "tabview", "tabitem", "expander" });
            }
        }

        if (version >= 6)
        {
            UiRemoteSnapshot snapshot = JsonSerializer.Deserialize(bytes, UiAutomationJsonContext.Default.UiRemoteSnapshot)!;
            Assert.Equal(0.25, snapshot.Nodes.Single(node => node.TestId == "numeric").Numeric!.Value);
            Assert.Equal(version == 8 ? true : null, snapshot.Nodes.Single(node => node.TestId == "details").IsExpanded);
        }

        await job.ReleaseAsync();
    }
}
