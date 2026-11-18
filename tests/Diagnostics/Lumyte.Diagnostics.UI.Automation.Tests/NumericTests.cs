using System.Text.Json;
using Lumyte.Diagnostics.UI.Automation.Client;
using Lumyte.Diagnostics.UI.Automation.Contracts;
using Lumyte.UI.Controls;
using Xunit;
using static Lumyte.UI.Compose;
using static Lumyte.UI.Controls.Compose;

namespace Lumyte.Diagnostics.UI.Automation.Tests;

/// <summary>Checks numeric commands through actual runtime, HTTP client and strict frozen readers.</summary>
public sealed class NumericTests
{
    /// <summary>All old readers keep exact field counts and never see new numeric roles or capabilities.</summary>
    /// <param name="version">The old requested version.</param>
    /// <returns>The asynchronous check.</returns>
    [Trait("Execution", "Integration")]
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public async Task StrictOldReadersKeepTheirOriginalShapeAsync(int version)
    {
        using var handler = new LoopbackHandler(configureSurface: surface => surface.Rebuild(Stack()[
            Slider(min: -100, max: 100, value: 50, testId: "scalar"),
            RangeSlider(value: new SliderRange(0.2, 0.8), testId: "range")]));
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
        else
        {
            Assert.NotNull(JsonSerializer.Deserialize(bytes, FrozenV4Context.Default.FrozenV4Snapshot));
        }

        using var raw = JsonDocument.Parse(bytes);
        foreach (JsonElement node in raw.RootElement.GetProperty("nodes").EnumerateArray())
        {
            Assert.Equal(version < 4 ? 18 : 23, node.EnumerateObject().Count());
            Assert.False(node.TryGetProperty("numeric", out _));
            Assert.DoesNotContain(node.GetProperty("role").GetString(), new[] { "slider", "rangeslider" });
            Assert.DoesNotContain("set-value", node.GetProperty("actions").EnumerateArray().Select(action => action.GetString()));
            Assert.DoesNotContain("set-range", node.GetProperty("actions").EnumerateArray().Select(action => action.GetString()));
        }

        await job.ReleaseAsync();
    }

    /// <summary>Actual closed commands preserve atomic endpoints, input authority and exact numeric semantics.</summary>
    /// <returns>The asynchronous check.</returns>
    [Trait("Execution", "Integration")]
    [Fact]
    public async Task RemoteNumericCommandsUseRealControlsAndExactMetadataAsync()
    {
        int changes = 0;
        using var handler = new LoopbackHandler(configureSurface: surface => surface.Rebuild(Stack()[
            Slider(min: -100, max: 100, step: 1, testId: "scalar", onChanged: _ => changes++),
            RangeSlider(min: -100, max: 100, step: 1, testId: "range")]));
        using var http = new HttpClient(handler) { BaseAddress = new("http://diagnostics/") };
        await using var client = new UiAutomationDiagnosticsClient(http, await handler.Ready);
        UiRemoteTarget target = Assert.Single(await client.ListTargetsAsync());
        UiRemotePage observe = client.Page(target.Id);
        await Assert.ThrowsAsync<InvalidOperationException>(() => observe.GetByTestId("scalar").SetValueAsync(42));
        await using UiRemoteControl control = await client.AcquireControlAsync(target.Id);
        await control.Page.GetByTestId("scalar").SetValueAsync(42);
        await control.Page.GetByTestId("scalar").SetValueAsync(42);
        await control.Page.GetByTestId("range").SetRangeAsync(-20, 80);
        Assert.Equal(1, changes);
        UiRemoteJob job = await observe.StartObservationAsync(new(6, "snapshot", []));
        UiRemoteJobStatus status = await job.WaitAsync();
        UiRemoteSnapshot snapshot = JsonSerializer.Deserialize(await job.DownloadArtifactAsync(status.ResultArtifactId!.Value), UiAutomationJsonContext.Default.UiRemoteSnapshot)!;
        UiRemoteNode scalar = snapshot.Nodes.Single(node => node.TestId == "scalar");
        Assert.Equal(42, scalar.Numeric!.Value);
        Assert.Equal(0.71, scalar.Value!.Value, 12);
        Assert.Contains("set-value", scalar.Actions);
        UiRemoteNode range = snapshot.Nodes.Single(node => node.TestId == "range");
        Assert.Equal(-20, range.Numeric!.Lower);
        Assert.Equal(80, range.Numeric.Upper);
        Assert.Null(range.Value);
        await job.ReleaseAsync();
    }
}
