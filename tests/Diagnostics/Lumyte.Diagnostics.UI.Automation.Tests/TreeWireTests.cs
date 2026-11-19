using System.Text.Json;
using Lumyte.Diagnostics.UI.Automation.Client;
using Lumyte.Diagnostics.UI.Automation.Contracts;
using Lumyte.UI.Collections;
using Xunit;
using static Lumyte.UI.Collections.Compose;
using static Lumyte.UI.Compose;

namespace Lumyte.Diagnostics.UI.Automation.Tests;

/// <summary>Tree operations use the actual HTTP client, owner runtime and exclusive input lease.</summary>
public sealed class TreeWireTests
{
    /// <summary>Observation does not discover children; leased path reveal exposes truthful hierarchy.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task ObservationsDoNotDiscoverButLeasedKeyRevealDoesAsync()
    {
        int reads = 0;
        using var handler = new LoopbackHandler(configureSurface: surface => surface.Rebuild(TreeView<string>(
            roots: new[] { "root" },
            keySelector: key => key,
            childrenSelector: _ =>
            {
                Interlocked.Increment(ref reads);
                return Enumerable.Range(0, 1000).Select(index => "child" + index).ToArray();
            },
            hasChildren: key => key == "root",
            keyPathSelector: key => new[] { "root", key },
            itemTemplate: _ => SizedBox(height: 20),
            testId: "tree",
            height: 60,
            virtualization: new UiItemVirtualization(20, 0))))
        {
            SchemaVersion = 11,
        };
        using var http = new HttpClient(handler) { BaseAddress = new("http://diagnostics/") };
        await using var client = new UiAutomationDiagnosticsClient(http, await handler.Ready);
        UiRemoteTarget target = Assert.Single(await client.ListTargetsAsync());
        UiRemoteLocator observed = client.Page(target.Id).GetByRole("tree").GetByItemKey("child999");
        Assert.Equal(0, await observed.CountAsync());
        Assert.Equal(0, Volatile.Read(ref reads));
        await Assert.ThrowsAsync<InvalidOperationException>(() => observed.BringIntoViewAsync());
        await using UiRemoteControl control = await client.AcquireControlAsync(target.Id);
        UiRemoteLocator item = control.Page.GetByRole("tree").GetByItemKey("child999");
        await item.BringIntoViewAsync();
        await control.Page.GetByRole("tree").SelectOptionAsync("child999");
        Assert.Equal(1, Volatile.Read(ref reads));
        UiRemoteJob job = await control.Page.StartObservationAsync(new(11, "snapshot", []));
        UiRemoteJobStatus status = await job.WaitAsync();
        byte[] bytes = await job.DownloadArtifactAsync(status.ResultArtifactId!.Value);
        UiRemoteSnapshot snapshot = JsonSerializer.Deserialize(bytes, UiAutomationJsonContext.Default.UiRemoteSnapshot)!;
        UiRemoteNode child = Assert.Single(snapshot.Nodes, node => node.OptionKey == "child999");
        Assert.Equal(1, child.Depth);
        Assert.Equal("root", child.ParentKey);
        Assert.True(child.IsSelected);
        await job.ReleaseAsync();
    }

    /// <summary>Tree hierarchy fields never alter frozen schema one through ten node shapes.</summary>
    /// <param name="version">The frozen schema version.</param>
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
    [InlineData(9)]
    [InlineData(10)]
    public async Task OldSnapshotShapesRemainFrozenAsync(int version)
    {
        using var handler = new LoopbackHandler(configureSurface: surface => surface.Rebuild(TreeView<string>(
            roots: new[] { "root" },
            keySelector: key => key,
            childrenSelector: _ => Array.Empty<string>(),
            hasChildren: _ => true,
            itemTemplate: _ => SizedBox(height: 20))))
        {
            SchemaVersion = 11,
        };
        using var http = new HttpClient(handler) { BaseAddress = new("http://diagnostics/") };
        await using var client = new UiAutomationDiagnosticsClient(http, await handler.Ready);
        UiRemoteTarget target = Assert.Single(await client.ListTargetsAsync());
        UiRemoteJob job = await client.Page(target.Id).StartObservationAsync(new(version, "snapshot", []));
        UiRemoteJobStatus status = await job.WaitAsync();
        byte[] bytes = await job.DownloadArtifactAsync(status.ResultArtifactId!.Value);
        using var raw = JsonDocument.Parse(bytes);
        foreach (JsonElement node in raw.RootElement.GetProperty("nodes").EnumerateArray())
        {
            Assert.Equal(version < 4 ? 18 : version < 6 ? 23 : version < 8 ? 24 : 25, node.EnumerateObject().Count());
            Assert.DoesNotContain(node.GetProperty("role").GetString(), new[] { "tree", "treeitem" });
            Assert.False(node.TryGetProperty("depth", out _));
            Assert.False(node.TryGetProperty("parentKey", out _));
        }

        if (version >= 8)
        {
            Assert.NotNull(JsonSerializer.Deserialize(bytes, FrozenV8Context.Default.FrozenV8Snapshot));
        }

        await job.ReleaseAsync();
    }

    /// <summary>Earlier schema versions reject hierarchy role queries before execution.</summary>
    [Fact]
    public void TreeRolesRequireEleven()
    {
        for (int version = 1; version <= 10; version++)
        {
            Assert.Throws<JsonException>(() => UiRemoteSchema.SerializePlan(new(version, "count", [new("role", Role: "tree")])));
        }

        Assert.Equal(11, UiRemoteSchema.ParsePlan(UiRemoteSchema.SerializePlan(new(11, "count", [new("role", Role: "treeitem")]))).SchemaVersion);
    }
}
