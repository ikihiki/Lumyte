using System.Text.Json;
using Lumyte.Diagnostics.UI.Automation.Client;
using Lumyte.Diagnostics.UI.Automation.Contracts;
using Lumyte.UI;
using Lumyte.UI.Collections;
using Xunit;
using static Lumyte.UI.Collections.Compose;
using static Lumyte.UI.Compose;

namespace Lumyte.Diagnostics.UI.Automation.Tests;

/// <summary>Grid semantics and input use the real owner runtime through the HTTP client.</summary>
public sealed class DataGridWireTests
{
    /// <summary>Observation never constructs an offscreen row; control reveals and selects a real cell.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task LeasedCellRevealSelectionAndHeaderInputAsync()
    {
        int factories = 0;
        Lumyte.UI.Compose.Definitions.Widget Cell(DataGridCellContext<int> cell, Lumyte.UI.Collections.Compose.Definitions.DataGrid<int> owner)
        {
            Interlocked.Increment(ref factories);
            return SizedBox();
        }

        using var handler = new LoopbackHandler(configureSurface: surface => surface.Rebuild(CreateGrid(Cell))) { SchemaVersion = 12 };
        using var http = new HttpClient(handler) { BaseAddress = new("http://diagnostics/") };
        await using var client = new UiAutomationDiagnosticsClient(http, await handler.Ready);
        UiRemoteTarget target = Assert.Single(await client.ListTargetsAsync());
        UiRemoteLocator observed = client.Page(target.Id).GetByRole("datagrid").GetByItemKey("999").GetByColumnKey("value");
        int before = Volatile.Read(ref factories);
        Assert.Equal(0, await observed.CountAsync());
        Assert.Equal(before, Volatile.Read(ref factories));
        await Assert.ThrowsAsync<InvalidOperationException>(() => observed.BringIntoViewAsync());
        await using UiRemoteControl control = await client.AcquireControlAsync(target.Id);
        int beforeFill = Volatile.Read(ref factories);
        UiRemoteAutomationException editing = await Assert.ThrowsAsync<UiRemoteAutomationException>(() => control.Page.GetByRole("datagrid").GetByItemKey("999").GetByColumnKey("value").FillAsync("not an editor"));
        Assert.Equal("text-input-disabled", editing.Code);
        Assert.Equal(beforeFill, Volatile.Read(ref factories));
        await control.Page.GetByRole("button", "Value").ClickAsync();
        await control.Page.GetByRole("button", "Value").PressAsync("ArrowRight");
        UiRemoteLocator cellTarget = control.Page.GetByRole("datagrid").GetByItemKey("999").GetByColumnKey("value");
        await cellTarget.BringIntoViewAsync();
        await cellTarget.FocusAsync();
        UiRemoteJob focusJob = await control.Page.StartObservationAsync(new(12, "snapshot", []));
        UiRemoteJobStatus focusStatus = await focusJob.WaitAsync();
        byte[] focusedBytes = await focusJob.DownloadArtifactAsync(focusStatus.ResultArtifactId!.Value);
        UiRemoteSnapshot focusedSnapshot = JsonSerializer.Deserialize(focusedBytes, UiAutomationJsonContext.Default.UiRemoteSnapshot)!;
        Assert.Null(Assert.Single(focusedSnapshot.Nodes, node => node.Role == "datagrid").SelectedKey);
        Assert.Equal("999", Assert.Single(focusedSnapshot.Nodes, node => node.IsFocused).GridCell?.RowKey);
        await focusJob.ReleaseAsync();
        await cellTarget.ClickAsync();
        await control.Page.GetByRole("datagrid").SelectOptionAsync("998");
        UiRemoteJob job = await control.Page.StartObservationAsync(new(12, "snapshot", []));
        UiRemoteJobStatus status = await job.WaitAsync();
        byte[] bytes = await job.DownloadArtifactAsync(status.ResultArtifactId!.Value);
        UiRemoteSnapshot snapshot = JsonSerializer.Deserialize(bytes, UiAutomationJsonContext.Default.UiRemoteSnapshot)!;
        UiRemoteNode cell = Assert.Single(snapshot.Nodes, node => node.GridCell is { RowKey: "998", ColumnKey: "value" });
        UiRemoteNode row = Assert.Single(snapshot.Nodes, node => node.Id == cell.ParentId);
        Assert.Equal("gridrow", row.Role);
        Assert.Equal("998", row.OptionKey);
        Assert.True(cell.IsSelected);
        Assert.DoesNotContain("edit", cell.Actions);
        Assert.InRange(Volatile.Read(ref factories), 1, 100);
        await job.ReleaseAsync();
    }

    /// <summary>All eleven prior versions retain their exact top-level node field counts.</summary>
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
    [InlineData(11)]
    public async Task EarlierSnapshotsRemainFrozenAsync(int version)
    {
        using var handler = new LoopbackHandler(configureSurface: surface => surface.Rebuild(CreateGrid(Cell))) { SchemaVersion = 12 };
        using var http = new HttpClient(handler) { BaseAddress = new("http://diagnostics/") };
        await using var client = new UiAutomationDiagnosticsClient(http, await handler.Ready);
        UiRemoteTarget target = Assert.Single(await client.ListTargetsAsync());
        UiRemoteJob job = await client.Page(target.Id).StartObservationAsync(new(version, "snapshot", []));
        UiRemoteJobStatus status = await job.WaitAsync();
        byte[] bytes = await job.DownloadArtifactAsync(status.ResultArtifactId!.Value);
        using var raw = JsonDocument.Parse(bytes);
        foreach (JsonElement node in raw.RootElement.GetProperty("nodes").EnumerateArray())
        {
            Assert.Equal(version < 4 ? 18 : version < 6 ? 23 : version < 8 ? 24 : version < 11 ? 25 : 27, node.EnumerateObject().Count());
            Assert.DoesNotContain(node.GetProperty("role").GetString(), new[] { "datagrid", "gridrow", "gridcell" });
            Assert.False(node.TryGetProperty("gridCell", out _));
        }

        await job.ReleaseAsync();
    }

    /// <summary>Older catalogs reject every grid operation before an HTTP action is submitted.</summary>
    /// <param name="operation">The typed operation.</param>
    /// <returns>The asynchronous check.</returns>
    [Theory]
    [InlineData("scroll")]
    [InlineData("check")]
    [InlineData("uncheck")]
    [InlineData("set-value")]
    [InlineData("set-range")]
    [InlineData("expand")]
    [InlineData("fill")]
    [InlineData("reveal")]
    public async Task GridAncestorRequiresTwelveBeforeMutationAsync(string operation)
    {
        using var handler = new LoopbackHandler() { SchemaVersion = 11 };
        using var http = new HttpClient(handler) { BaseAddress = new("http://diagnostics/") };
        await using var client = new UiAutomationDiagnosticsClient(http, await handler.Ready);
        UiRemoteTarget target = Assert.Single(await client.ListTargetsAsync());
        await using UiRemoteControl control = await client.AcquireControlAsync(target.Id);
        UiRemoteLocator grid = control.Page.GetByRole("datagrid").GetByTestId("save");
        Task Run() => operation switch
        {
            "scroll" => grid.ScrollAsync(new(0, 1)),
            "check" => grid.CheckAsync(),
            "uncheck" => grid.UncheckAsync(),
            "set-value" => grid.SetValueAsync(0.5),
            "set-range" => grid.SetRangeAsync(0.2, 0.8),
            "expand" => grid.ExpandAsync(),
            "fill" => grid.FillAsync("not sent"),
            _ => grid.GetByItemKey("999").GetByColumnKey("value").BringIntoViewAsync(),
        };
        UiRemoteAutomationException failure = await Assert.ThrowsAsync<UiRemoteAutomationException>(Run);
        Assert.Equal("unsupported-catalog", failure.Code);
        Assert.Empty(handler.ActionBodies);
    }

    private static string Key(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static Lumyte.UI.Compose.Definitions.Widget Header(DataGridColumn<int> column, Lumyte.UI.Collections.Compose.Definitions.DataGrid<int> owner) => SizedBox();

    private static Lumyte.UI.Compose.Definitions.Widget Cell(DataGridCellContext<int> cell, Lumyte.UI.Collections.Compose.Definitions.DataGrid<int> owner) => SizedBox();

    private static Lumyte.UI.Collections.Compose.Definitions.DataGrid<int> CreateGrid(Lumyte.Composition.CompositionTemplate<DataGridCellContext<int>, Lumyte.UI.Collections.Compose.Definitions.DataGrid<int>, Lumyte.UI.Compose.Definitions.Widget> cell) => DataGrid<int>(items: Enumerable.Range(0, 1000).ToArray(), keySelector: Key, columns: new[] { new DataGridColumn<int>("value", "Value", comparer: Comparer<int>.Default) }, height: 100, virtualization: new UiItemVirtualization(20, 1))[
        DataGridFactory<int>().Header()[Header], DataGridFactory<int>().Cell()[cell]];
}
