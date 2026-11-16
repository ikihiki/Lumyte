using System.Text.Json;
using Lumyte.Diagnostics.UI.Automation.Client;
using Lumyte.Diagnostics.UI.Automation.Contracts;
using Lumyte.UI;
using Xunit;
using static Lumyte.UI.Compose;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.Diagnostics.UI.Automation.Tests;

/// <summary>Checks read-only media semantics through runtime mapping and the typed wire client.</summary>
public sealed class SemanticValueTests
{
    /// <summary>Checks all new roles roundtrip, locate correctly, and retain semantic value/state.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task ClientCarriesReadOnlySemanticStateAsync()
    {
        using var handler = new LoopbackHandler(configureSurface: surface => surface.Rebuild(Panel()[
            new SemanticWidget { Role = UiRole.Text },
            new SemanticWidget { Role = UiRole.Image },
            new SemanticWidget { Role = UiRole.Icon },
            new SemanticWidget { Role = UiRole.ProgressBar, Value = 0.375 },
            new SemanticWidget { Role = UiRole.BusyIndicator, Indeterminate = true }]));
        using var http = new HttpClient(handler) { BaseAddress = new("http://diagnostics/") };
        await using var client = new UiAutomationDiagnosticsClient(http, await handler.Ready);
        UiRemoteTarget target = Assert.Single(await client.ListTargetsAsync());
        UiRemotePage page = client.Page(target.Id);
        foreach (string role in new[] { "text", "image", "icon", "progressbar", "busyindicator" })
        {
            Assert.Equal(1, await page.GetByRole(role).CountAsync());
        }

        Assert.Throws<ArgumentException>(() => page.GetByRole("progress-bar"));
        UiRemoteJob job = await page.StartObservationAsync(new(1, "snapshot", []));
        UiRemoteJobStatus status = await job.WaitAsync();
        Assert.Equal("Succeeded", status.State);
        byte[] bytes = await job.DownloadArtifactAsync(status.ResultArtifactId!.Value);
        UiRemoteSnapshot snapshot = JsonSerializer.Deserialize(bytes, UiAutomationJsonContext.Default.UiRemoteSnapshot)!;
        UiRemoteNode progress = snapshot.Nodes.Single(node => node.Role == "progressbar");
        Assert.Equal(0.375, progress.Value);
        Assert.False(progress.IsIndeterminate);
        UiRemoteNode busy = snapshot.Nodes.Single(node => node.Role == "busyindicator");
        Assert.Null(busy.Value);
        Assert.True(busy.IsIndeterminate);
        Assert.All(snapshot.Nodes, node => Assert.Empty(node.Actions));
        Assert.Equal(0, handler.Clicks);
        await job.ReleaseAsync();
    }

    /// <summary>Checks added snapshot fields remain optional for earlier payloads and unknown operations stay closed.</summary>
    [Fact]
    public void OptionalFieldsPreserveEarlierPayloadsAndClosedPlans()
    {
        var node = new UiRemoteNode("1", null, null, null, "image", new(0, 0, 1, 1), new(0, 0, 1, 1), true, true, true, false, false, false, false, []);
        string json = JsonSerializer.Serialize(new UiRemoteSnapshot(Guid.NewGuid(), "1", [node]), UiAutomationJsonContext.Default.UiRemoteSnapshot);
        json = json.Replace(",\"value\":null", string.Empty, StringComparison.Ordinal).Replace(",\"isIndeterminate\":false", string.Empty, StringComparison.Ordinal);
        UiRemoteNode parsed = Assert.Single(JsonSerializer.Deserialize(json, UiAutomationJsonContext.Default.UiRemoteSnapshot)!.Nodes);
        Assert.Null(parsed.Value);
        Assert.False(parsed.IsIndeterminate);
        Assert.Throws<JsonException>(() => UiRemoteSchema.SerializePlan(new(1, "set-value", [new("role", Role: "progressbar")])));
        Assert.Throws<JsonException>(() => UiRemoteSchema.SerializePlan(new(1, "count", [new("role", Role: "unknown")])));
    }

    private sealed class SemanticWidget : Widget
    {
        internal double? Value { get; init; }

        internal bool Indeterminate { get; init; }

        protected override UiElement CreateElement() => new SemanticElement(this);
    }

    private sealed class SemanticElement(SemanticWidget widget) : UiElement<SemanticWidget>(widget)
    {
        protected override UiAutomationActions AutomationActions => UiAutomationActions.None;

        protected override double? AutomationValue => Widget.Value;

        protected override bool IsAutomationIndeterminate => Widget.Indeterminate;
    }
}
