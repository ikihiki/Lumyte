using System.Text.Json;
using Lumyte.Diagnostics.UI.Automation.Client;
using Lumyte.Diagnostics.UI.Automation.Contracts;
using Xunit;
using static Lumyte.UI.Collections.Compose;
using static Lumyte.UI.Compose;

namespace Lumyte.Diagnostics.UI.Automation.Tests;

/// <summary>Freezes old snapshot shapes while exposing actual collection roles in seven.</summary>
public sealed class CollectionWireTests
{
    /// <summary>Accepts new roles only in the new schema.</summary>
    [Fact]
    public void CollectionRoleRequiresSeven()
    {
        Assert.Throws<JsonException>(() => UiRemoteSchema.ParsePlan("{\"schemaVersion\":6,\"kind\":\"count\",\"locator\":[{\"kind\":\"role\",\"role\":\"listbox\"}]}"));
        Assert.Equal(7, UiRemoteSchema.ParsePlan("{\"schemaVersion\":7,\"kind\":\"count\",\"locator\":[{\"kind\":\"role\",\"role\":\"listbox\"}]}").SchemaVersion);
    }

    /// <summary>The actual runtime uses the ordinary item hit probe and exclusive input lease.</summary>
    /// <returns>The asynchronous check.</returns>
    [Trait("Execution", "Integration")]
    [Fact]
    public async Task RemoteListSelectionUsesNormalInputAndLatestCallbackAsync()
    {
        int changes = 0;
        using var handler = new LoopbackHandler(configureSurface: surface => surface.Rebuild(ListBox<string>(items: new[] { "a", "b" }, keySelector: item => item, itemTemplate: _ => SizedBox(width: 100, height: 20), testId: "list", onSelectionChanged: _ => changes++)));
        using var http = new HttpClient(handler) { BaseAddress = new("http://diagnostics/") };
        await using var client = new UiAutomationDiagnosticsClient(http, await handler.Ready);
        UiRemoteTarget target = Assert.Single(await client.ListTargetsAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.Page(target.Id).GetByTestId("list").SelectOptionAsync("b"));
        await using UiRemoteControl control = await client.AcquireControlAsync(target.Id);
        Assert.Equal(1, await control.Page.GetByRole("listbox").CountAsync());
        await control.Page.GetByRole("listbox").SelectOptionAsync("b");
        await control.Page.GetByRole("listbox").SelectOptionAsync("b");
        Assert.Equal(1, changes);
    }

    /// <summary>Old exact fields stay frozen and numeric six remains available.</summary>
    /// <param name="version">The requested schema.</param>
    /// <returns>The asynchronous check.</returns>
    [Trait("Execution", "Integration")]
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public async Task CollectionsDoNotChangeOldSnapshotShapesAsync(int version)
    {
        using var handler = new LoopbackHandler(configureSurface: surface => surface.Rebuild(Stack()[ListBox<string>(items: new[] { "a", "b" }, keySelector: item => item, itemTemplate: _ => SizedBox(width: 100, height: 20), testId: "list"), Lumyte.UI.Controls.Compose.Slider(value: 0.25, testId: "numeric")]));
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
        else
        {
            Assert.NotNull(JsonSerializer.Deserialize(bytes, FrozenV6Context.Default.FrozenV6Snapshot));
        }

        using var raw = JsonDocument.Parse(bytes);
        foreach (JsonElement node in raw.RootElement.GetProperty("nodes").EnumerateArray())
        {
            Assert.Equal(version < 4 ? 18 : version < 6 ? 23 : 24, node.EnumerateObject().Count());
            if (version < 7)
            {
                Assert.DoesNotContain(node.GetProperty("role").GetString(), new[] { "listbox", "listitem", "combobox" });
            }
        }

        if (version >= 6)
        {
            UiRemoteSnapshot snapshot = JsonSerializer.Deserialize(bytes, UiAutomationJsonContext.Default.UiRemoteSnapshot)!;
            Assert.Equal(0.25, snapshot.Nodes.Single(node => node.TestId == "numeric").Numeric!.Value);
        }

        await job.ReleaseAsync();
    }
}
