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
            new SemanticWidget { Role = UiRole.Text, Checked = null, Selected = true, Group = new(surface.RootElement.AutomationId.SurfaceId, ulong.MaxValue), Option = "日本語", Selection = "日本語" },
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
        UiRemoteJob job = await page.StartObservationAsync(new(4, "snapshot", []));
        UiRemoteJobStatus status = await job.WaitAsync();
        Assert.Equal("Succeeded", status.State);
        byte[] bytes = await job.DownloadArtifactAsync(status.ResultArtifactId!.Value);
        using var raw = JsonDocument.Parse(bytes);
        Assert.Equal("18446744073709551615", raw.RootElement.GetProperty("nodes").EnumerateArray().Single(node => node.GetProperty("role").GetString() == "text").GetProperty("selectionGroup").GetString());
        Assert.Equal(JsonValueKind.Null, raw.RootElement.GetProperty("nodes").EnumerateArray().Single(node => node.GetProperty("role").GetString() == "text").GetProperty("isChecked").ValueKind);
        foreach (JsonElement node in raw.RootElement.GetProperty("nodes").EnumerateArray())
        {
            Assert.Equal(23, node.EnumerateObject().Count());
            Assert.True(node.TryGetProperty("isChecked", out _));
            Assert.True(node.TryGetProperty("isSelected", out _));
            Assert.True(node.TryGetProperty("selectionGroup", out _));
            Assert.True(node.TryGetProperty("optionKey", out _));
            Assert.True(node.TryGetProperty("selectedKey", out _));
        }

        UiRemoteSnapshot snapshot = JsonSerializer.Deserialize(bytes, UiAutomationJsonContext.Default.UiRemoteSnapshot)!;
        UiRemoteNode progress = snapshot.Nodes.Single(node => node.Role == "progressbar");
        Assert.Equal(0.375, progress.Value);
        Assert.False(progress.IsIndeterminate);
        UiRemoteNode busy = snapshot.Nodes.Single(node => node.Role == "busyindicator");
        Assert.Null(busy.Value);
        Assert.True(busy.IsIndeterminate);
        Assert.All(snapshot.Nodes, node => Assert.Empty(node.Actions));
        UiRemoteNode choice = snapshot.Nodes.Single(node => node.Role == "text");
        Assert.Null(choice.IsChecked);
        Assert.True(choice.IsSelected);
        Assert.Equal("18446744073709551615", choice.SelectionGroup);
        Assert.Equal("日本語", choice.OptionKey);
        Assert.Equal("日本語", choice.SelectedKey);
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

    /// <summary>Checks desired actions never upgrade an observation page to input authority.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task SelectionRequiresAnExplicitControlPageAsync()
    {
        using var handler = new LoopbackHandler();
        using var http = new HttpClient(handler) { BaseAddress = new("http://diagnostics/") };
        await using var client = new UiAutomationDiagnosticsClient(http, await handler.Ready);
        UiRemotePage page = client.Page(Assert.Single(await client.ListTargetsAsync()).Id);
        Assert.Equal(0, await page.GetByRole("checkbox").CountAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => page.GetByTestId("save").CheckAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => page.GetByTestId("save").UncheckAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => page.GetByTestId("save").SelectOptionAsync("one"));
        Assert.Empty(handler.ActionBodies);
    }

    /// <summary>Old strict source-generated readers consume the original node shape only.</summary>
    /// <param name="version">The requested old schema.</param>
    /// <returns>The asynchronous check.</returns>
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task LegacySnapshotPreservesOldWireShapeAsync(int version)
    {
        using var handler = new LoopbackHandler(configureSurface: surface => surface.Rebuild(Panel()[
            new SemanticWidget { Role = UiRole.CheckBox, Checked = true, Selected = true, Option = "a", Actions = UiAutomationActions.Check | UiAutomationActions.SelectOption }]));
        using var http = new HttpClient(handler) { BaseAddress = new("http://diagnostics/") };
        await using var client = new UiAutomationDiagnosticsClient(http, await handler.Ready);
        UiRemotePage page = client.Page(Assert.Single(await client.ListTargetsAsync()).Id);
        UiRemoteJob job = await page.StartObservationAsync(new(version, "snapshot", []));
        UiRemoteJobStatus status = await job.WaitAsync();
        Assert.Equal("Succeeded", status.State);
        byte[] bytes = await job.DownloadArtifactAsync(status.ResultArtifactId!.Value);
        OldSnapshot snapshot = JsonSerializer.Deserialize(bytes, OldWireContext.Default.OldSnapshot)!;
        byte[] oldRoundTrip = JsonSerializer.SerializeToUtf8Bytes(snapshot, OldWireContext.Default.OldSnapshot);
        Assert.Equal(snapshot.Nodes.Length, JsonSerializer.Deserialize(oldRoundTrip, OldWireContext.Default.OldSnapshot)!.Nodes.Length);
        Assert.All(snapshot.Nodes, node =>
        {
            Assert.DoesNotContain(node.Role, new[] { "checkbox", "togglebutton", "switch", "radiobutton", "radiogroup" });
            Assert.DoesNotContain("check", node.Actions);
            Assert.DoesNotContain("select-option", node.Actions);
        });
        using var raw = JsonDocument.Parse(bytes);
        foreach (JsonElement node in raw.RootElement.GetProperty("nodes").EnumerateArray())
        {
            Assert.Equal(18, node.EnumerateObject().Count());
            Assert.False(node.TryGetProperty("isChecked", out _));
            Assert.False(node.TryGetProperty("isSelected", out _));
            Assert.False(node.TryGetProperty("selectionGroup", out _));
            Assert.False(node.TryGetProperty("optionKey", out _));
            Assert.False(node.TryGetProperty("selectedKey", out _));
        }

        await job.ReleaseAsync();
    }

    private sealed class SemanticWidget : Widget
    {
        internal UiAutomationActions Actions { get; init; }

        internal bool? Checked { get; init; }

        internal bool? Selected { get; init; }

        internal UiAutomationId? Group { get; init; }

        internal string? Option { get; init; }

        internal string? Selection { get; init; }

        internal double? Value { get; init; }

        internal bool Indeterminate { get; init; }

        protected override UiElement CreateElement() => new SemanticElement(this);
    }

    private sealed class SemanticElement(SemanticWidget widget) : UiElement<SemanticWidget>(widget)
    {
        protected override UiAutomationActions AutomationActions => Widget.Actions;

        protected override bool? AutomationChecked => Widget.Checked;

        protected override bool? AutomationSelected => Widget.Selected;

        protected override UiAutomationId? AutomationSelectionGroup => Widget.Group;

        protected override string? AutomationOptionKey => Widget.Option;

        protected override string? AutomationSelectedKey => Widget.Selection;

        protected override double? AutomationValue => Widget.Value;

        protected override bool IsAutomationIndeterminate => Widget.Indeterminate;
    }
}
