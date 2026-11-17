using System.Text.Json;
using Lumyte.Diagnostics.UI.Automation.Client;
using Lumyte.Diagnostics.UI.Automation.Contracts;
using Lumyte.UI.Controls;
using Xunit;
using static Lumyte.UI.Compose;
using static Lumyte.UI.Controls.Compose;

namespace Lumyte.Diagnostics.UI.Automation.Tests;

/// <summary>Freezes schemas one through eight while exposing menu roles in schema nine.</summary>
public sealed class MenuWireTests
{
    /// <summary>New roles and actions require eight; unknown fields remain closed.</summary>
    [Fact]
    public void MenuSchemaIsClosedAndVersioned()
    {
        var plan = new UiRemotePlan(9, "check", [new("role", Role: "menuitem")]);
        Assert.True(UiRemoteSchema.IsAction(UiRemoteSchema.ParsePlan(UiRemoteSchema.SerializePlan(plan))));
        Assert.Throws<JsonException>(() => UiRemoteSchema.SerializePlan(plan with { SchemaVersion = 8 }));
        Assert.Throws<JsonException>(() => UiRemoteSchema.ParsePlan("{\"schemaVersion\":8,\"kind\":\"expand\",\"locator\":[{\"kind\":\"role\",\"role\":\"expander\"}],\"isExpanded\":true}"));
    }

    /// <summary>Raw role queries are closed and require schema nine.</summary>
    /// <param name="role">A new closed role.</param>
    [Theory]
    [InlineData("menu")]
    [InlineData("menuitem")]
    [InlineData("tooltip")]
    public void RawRolesRequireSchemaNine(string role)
    {
        string tail = ",\"kind\":\"count\",\"locator\":[{\"kind\":\"role\",\"role\":\"" + role + "\"}]}";
        Assert.Throws<JsonException>(() => UiRemoteSchema.ParsePlan("{\"schemaVersion\":8" + tail));
        Assert.Equal(9, UiRemoteSchema.ParsePlan("{\"schemaVersion\":9" + tail).SchemaVersion);
        Assert.Throws<JsonException>(() => UiRemoteSchema.ParsePlan("{\"schemaVersion\":10" + tail));
    }

    /// <summary>Real menu Check uses exclusive ordinary input, not semantic-role inference.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task RemoteMenuCheckingIsLeasedAndIdempotentAsync()
    {
        int changes = 0;
        using var handler = new LoopbackHandler(configureSurface: surface => surface.Rebuild(Menu()[MenuItem(key: "check", isCheckable: true, onChanged: _ => changes++)]));
        using var http = new HttpClient(handler) { BaseAddress = new("http://diagnostics/") };
        await using var client = new UiAutomationDiagnosticsClient(http, await handler.Ready);
        UiRemoteTarget target = Assert.Single(await client.ListTargetsAsync());
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.Page(target.Id).GetByRole("menuitem").CheckAsync());
        await using UiRemoteControl control = await client.AcquireControlAsync(target.Id);
        await control.Page.GetByRole("menuitem").CheckAsync();
        await control.Page.GetByRole("menuitem").CheckAsync();
        await control.Page.GetByRole("menuitem").UncheckAsync();
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
    [InlineData(9)]
    public async Task MenuRolesKeepEveryOldShapeAsync(int version)
    {
        using var handler = new LoopbackHandler(configureSurface: surface => surface.Rebuild(Stack()[Menu(testId: "menu")[MenuItem(key: "a", testId: "item", isCheckable: true)], Expander(isExpanded: true, testId: "details")[Expander.Content()[Slider(value: 0.25, testId: "numeric")]]]));
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

        if (version == 8)
        {
            Assert.NotNull(JsonSerializer.Deserialize(bytes, FrozenV8Context.Default.FrozenV8Snapshot));
        }

        using var raw = JsonDocument.Parse(bytes);
        foreach (JsonElement node in raw.RootElement.GetProperty("nodes").EnumerateArray())
        {
            Assert.Equal(version < 4 ? 18 : version < 6 ? 23 : version < 8 ? 24 : 25, node.EnumerateObject().Count());
            if (version < 9)
            {
                Assert.DoesNotContain(node.GetProperty("role").GetString(), new[] { "menu", "menuitem", "tooltip" });
            }
        }

        if (version >= 6)
        {
            UiRemoteSnapshot snapshot = JsonSerializer.Deserialize(bytes, UiAutomationJsonContext.Default.UiRemoteSnapshot)!;
            Assert.Equal(0.25, snapshot.Nodes.Single(node => node.TestId == "numeric").Numeric!.Value);
            Assert.Equal(version >= 8 ? true : null, snapshot.Nodes.Single(node => node.TestId == "details").IsExpanded);
        }

        UiRemoteSnapshot typed = JsonSerializer.Deserialize(bytes, UiAutomationJsonContext.Default.UiRemoteSnapshot)!;
        Assert.Equal(version == 9 ? "menuitem" : "none", typed.Nodes.Single(node => node.TestId == "item").Role);
        await job.ReleaseAsync();
    }
}
