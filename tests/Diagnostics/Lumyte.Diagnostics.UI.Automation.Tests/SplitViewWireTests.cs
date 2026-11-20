using System.Text.Json;
using Lumyte.Diagnostics.UI.Automation.Client;
using Lumyte.Diagnostics.UI.Automation.Contracts;
using Xunit;
using static Lumyte.UI.Compose;

namespace Lumyte.Diagnostics.UI.Automation.Tests;

/// <summary>Runs typed splitter commands through the actual owner runtime and HTTP transport.</summary>
public sealed class SplitViewWireTests
{
    /// <summary>Observation is passive and a normal lease resizes the actual numeric separator.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    [Trait("Execution", "Integration")]
    public async Task LeasedDividerResizeAsync()
    {
        using var handler = new LoopbackHandler(configureSurface: surface => surface.Rebuild(SplitView()[SizedBox(), SizedBox(), SizedBox()])) { SchemaVersion = 13 };
        using var http = new HttpClient(handler) { BaseAddress = new("http://diagnostics/") };
        await using var client = new UiAutomationDiagnosticsClient(http, await handler.Ready);
        UiRemoteTarget target = Assert.Single(await client.ListTargetsAsync());
        UiRemoteLocator observed = client.Page(target.Id).GetByRole("splitview").GetByRole("separator", "Resize p:0 / p:1");
        Assert.Equal(1, await observed.CountAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => observed.SetValueAsync(0.6));
        await using UiRemoteControl control = await client.AcquireControlAsync(target.Id);
        UiRemoteLocator divider = control.Page.GetByRole("splitview").GetByRole("separator", "Resize p:0 / p:1");
        await divider.SetValueAsync(0.6);
        await divider.PressAsync("ArrowRight");
        UiRemoteJob job = await control.Page.StartObservationAsync(new(13, "snapshot", []));
        UiRemoteJobStatus status = await job.WaitAsync();
        byte[] bytes = await job.DownloadArtifactAsync(status.ResultArtifactId!.Value);
        UiRemoteSnapshot snapshot = JsonSerializer.Deserialize(bytes, UiAutomationJsonContext.Default.UiRemoteSnapshot)!;
        UiRemoteNode first = Assert.Single(snapshot.Nodes, node => node.Name == "Resize p:0 / p:1");
        UiRemoteNode owner = Assert.Single(snapshot.Nodes, node => node.Role == "splitview");
        Assert.Equal(owner.Id, first.ParentId);
        Assert.Contains("set-value", first.Actions);
        Assert.InRange(first.Numeric!.Value!.Value, 0.6, 1);
        Assert.Equal(2, snapshot.Nodes.Count(node => node.Role == "separator"));
        await job.ReleaseAsync();
    }

    /// <summary>All old schemas reject new role queries before execution.</summary>
    /// <param name="version">The frozen older schema.</param>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    public void OldSchemasRejectDividerRole(int version)
    {
        var plan = new UiRemotePlan(version, "count", [new("role", Role: "separator")]);
        string json = JsonSerializer.Serialize(plan, UiAutomationJsonContext.Default.UiRemotePlan);
        Assert.Throws<JsonException>(() => UiRemoteSchema.ParsePlan(json));
    }

    /// <summary>A schema-twelve catalog is rejected before any splitter action submission.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    [Trait("Execution", "Integration")]
    public async Task OldCatalogRejectsDividerCommandAsync()
    {
        using var handler = new LoopbackHandler(configureSurface: surface => surface.Rebuild(SplitView()[SizedBox(), SizedBox()])) { SchemaVersion = 12 };
        using var http = new HttpClient(handler) { BaseAddress = new("http://diagnostics/") };
        await using var client = new UiAutomationDiagnosticsClient(http, await handler.Ready);
        UiRemoteTarget target = Assert.Single(await client.ListTargetsAsync());
        await using UiRemoteControl control = await client.AcquireControlAsync(target.Id);
        UiRemoteAutomationException rejected = await Assert.ThrowsAsync<UiRemoteAutomationException>(() => control.Page.GetByRole("splitview").GetByRole("separator").SetValueAsync(0.6));
        Assert.Equal("unsupported-catalog", rejected.Code);
        Assert.Empty(handler.ActionBodies);
    }
}
