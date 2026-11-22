using Lumyte.Signals;
using Xunit;
using static Lumyte.UI.Collections.Compose;
using static Lumyte.UI.Compose;

namespace Lumyte.UI.Collections.Tests;

/// <summary>Checks concrete receiving owners and bounded row construction.</summary>
public sealed class DataGridTests
{
    /// <summary>Generated Header and Cell slots infer the concrete owner and replay onto new declarations.</summary>
    [Fact]
    public void TypedSlotsReplayOntoReceivingOwnerAndBoundConstruction()
    {
        var height = Signal.Create<float?>(100);
        var owners = new List<Compose.Definitions.DataGrid<int>>();
        int cells = 0;
        Lumyte.UI.Compose.Definitions.Widget Render(DataGridCellContext<int> cell, Compose.Definitions.DataGrid<int> owner)
        {
            owners.Add(owner);
            cells++;
            Assert.Equal("value", cell.Column.Key);
            Assert.Equal(cell.Row, cell.RowIndex);
            return SizedBox(height: 20);
        }

        Compose.Definitions.DataGrid<int> declaration = DataGrid<int>(
            items: Enumerable.Range(0, 1000).ToArray(),
            keySelector: value => value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            columns: new[] { new DataGridColumn<int>("value", "Value", comparer: Comparer<int>.Default) },
            height: height,
            virtualization: new UiItemVirtualization(20, 1))[
            DataGridFactory<int>().Header()[
                (column, owner) => SizedBox(height: 20)],
            DataGridFactory<int>().Cell()[Render]];
        Assert.Empty(owners);
        using var surface = new UiSurface(declaration);
        surface.SetViewport(new(200, 100));
        surface.UpdateLayout();
        DataGridElement<int> grid = Assert.IsType<DataGridElement<int>>(surface.RootElement);
        Assert.Equal(1000, grid.Keys.Count);
        Assert.InRange(cells, 1, 20);
        Assert.All(owners, owner => Assert.Same(declaration, owner));
        height.Value = 120;
        surface.UpdateLayout();
        Assert.NotSame(declaration, grid.Widget);
        Assert.Same(grid.Widget, owners.Last());
    }

    /// <summary>Ordering is stable, source-owned rows are not reordered, and none restores source order.</summary>
    [Fact]
    public void StableProjectionDoesNotMutateSource()
    {
        int[] source = [3, 1, 2];
        Lumyte.UI.Compose.Definitions.Widget Header(DataGridColumn<int> column, Compose.Definitions.DataGrid<int> owner) => SizedBox();
        Lumyte.UI.Compose.Definitions.Widget Cell(DataGridCellContext<int> cell, Compose.Definitions.DataGrid<int> owner) => SizedBox();
        Compose.Definitions.DataGrid<int> declaration = DataGrid<int>(items: source, keySelector: value => value.ToString(System.Globalization.CultureInfo.InvariantCulture), columns: new[] { new DataGridColumn<int>("value", "Value", comparer: Comparer<int>.Default) })[
            DataGridFactory<int>().Header()[Header],
            DataGridFactory<int>().Cell()[Cell]];
        using var surface = new UiSurface(declaration);
        surface.SetViewport(new(200, 100));
        surface.UpdateLayout();
        DataGridElement<int> grid = Assert.IsType<DataGridElement<int>>(surface.RootElement);
        Assert.True(grid.CycleSort("value"));
        surface.UpdateLayout();
        Assert.Equal(new[] { "1", "2", "3" }, grid.Keys);
        Assert.Equal(new[] { 3, 1, 2 }, source);
        Assert.True(grid.CycleSort("value"));
        surface.UpdateLayout();
        Assert.Equal(new[] { "3", "2", "1" }, grid.Keys);
        Assert.True(grid.CycleSort("value"));
        surface.UpdateLayout();
        Assert.Equal(new[] { "3", "1", "2" }, grid.Keys);
    }

    /// <summary>Cells have actual row parents, ordinary keyboard focus and no editable capability.</summary>
    [Fact]
    public void CellSemanticsAndKeyboardUseOneInputOwner()
    {
        Compose.Definitions.DataGrid<int> declaration = CreateGrid();
        using var surface = new UiSurface(declaration);
        surface.SetViewport(new(240, 100));
        surface.UpdateLayout();
        UiAutomationSnapshot snapshot = surface.CaptureAutomationSnapshot();
        UiAutomationNode first = snapshot.Nodes.First(node => node.GridCell is { RowIndex: 0, ColumnIndex: 0 });
        Assert.Equal(UiRole.DataGrid, snapshot.Nodes[0].Role);
        Assert.DoesNotContain(snapshot.Nodes, node => node.Role == UiRole.Group && (node.Actions & UiAutomationActions.SelectOption) != 0);
        Assert.DoesNotContain(snapshot.Nodes.Where(node => node.Role == UiRole.GridRow), node => node.IsFocusable || node.Actions != UiAutomationActions.None);
        Assert.Equal(UiAutomationActions.Click | UiAutomationActions.Focus, first.Actions);
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(first.ActionableBounds.X + 5, first.ActionableBounds.Y + 5)));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(first.ActionableBounds.X + 5, first.ActionableBounds.Y + 5)));
        Assert.Equal("0", Assert.IsType<DataGridElement<int>>(surface.RootElement).SelectedKey);
        surface.Dispatch(new UiKeyInput(Lumyte.Input.Abstractions.Key.ArrowRight, true));
        surface.Dispatch(new UiKeyInput(Lumyte.Input.Abstractions.Key.ArrowRight, false));
        Assert.Equal(new UiGridCellState("0", "second", 0, 1), surface.CaptureAutomationSnapshot().Nodes.Single(node => node.IsFocused).GridCell);
        surface.Dispatch(new UiKeyInput(Lumyte.Input.Abstractions.Key.ArrowDown, true));
        surface.Dispatch(new UiKeyInput(Lumyte.Input.Abstractions.Key.ArrowDown, false));
        Assert.Equal(new UiGridCellState("1", "second", 1, 1), surface.CaptureAutomationSnapshot().Nodes.Single(node => node.IsFocused).GridCell);
    }

    /// <summary>Argument-only replay keeps accepted sort and widths while rebinding the concrete owner.</summary>
    [Fact]
    public void RecipeReplayKeepsAcceptedStateAndRebindsOwner()
    {
        var height = Signal.Create<float?>(100);
        var selection = Signal.Create<string?>("2");
        var owners = new List<Compose.Definitions.DataGrid<int>>();
        Lumyte.UI.Compose.Definitions.Widget Render(DataGridCellContext<int> cell, Compose.Definitions.DataGrid<int> owner)
        {
            owners.Add(owner);
            return SizedBox();
        }

        Compose.Definitions.DataGrid<int> declaration = DataGrid<int>(items: new[] { 2, 0, 1 }, keySelector: Key, columns: Columns(), height: height, selectedKey: selection)[
            DataGridFactory<int>().Header()[Header], DataGridFactory<int>().Cell()[Render]];
        using var surface = new UiSurface(declaration);
        surface.SetViewport(new(240, 120));
        surface.UpdateLayout();
        DataGridElement<int> grid = Assert.IsType<DataGridElement<int>>(surface.RootElement);
        grid.CycleSort("first");
        grid.ResizeColumn("first", 80);
        surface.UpdateLayout();
        height.Value = 120;
        surface.UpdateLayout();
        Assert.Equal(DataGridSortDirection.Ascending, grid.Sort.Direction);
        Assert.Equal(80, grid.ColumnWidths[0]);
        Assert.Equal(new[] { "0", "1", "2" }, grid.Keys);
        Assert.NotSame(declaration, grid.Widget);
        Assert.Same(grid.Widget, owners.Last());
        Assert.Equal("2", grid.SelectedKey);
        selection.Value = "1";
        surface.UpdateLayout();
        Assert.Equal("1", grid.SelectedKey);
        Assert.Equal(DataGridSortDirection.Ascending, grid.Sort.Direction);
        Assert.Equal(80, grid.ColumnWidths[0]);
        Assert.Same(grid.Widget, owners.Last());
    }

    /// <summary>A failed projected cell factory publishes neither sort nor width nor identities.</summary>
    [Fact]
    public void FailedSortResizePreparationIsAtomic()
    {
        bool fail = false;
        int sorts = 0;
        int widths = 0;
        Lumyte.UI.Compose.Definitions.Widget Render(DataGridCellContext<int> cell, Compose.Definitions.DataGrid<int> owner) => fail ? throw new InvalidOperationException("cell") : SizedBox();
        Compose.Definitions.DataGrid<int> declaration = DataGrid<int>(items: new[] { 2, 1, 0 }, keySelector: Key, columns: Columns(), onSortChanged: _ => sorts++, onColumnWidthChanged: (_, _) => widths++)[
            DataGridFactory<int>().Header()[Header], DataGridFactory<int>().Cell()[Render]];
        using var surface = new UiSurface(declaration);
        surface.SetViewport(new(240, 100));
        surface.UpdateLayout();
        DataGridElement<int> grid = Assert.IsType<DataGridElement<int>>(surface.RootElement);
        UiAutomationSnapshot before = surface.CaptureAutomationSnapshot();
        fail = true;
        grid.CycleSort("first");
        grid.ResizeColumn("first", 180);
        Assert.Throws<InvalidOperationException>(surface.UpdateLayout);
        Assert.Equal(new[] { "2", "1", "0" }, grid.Keys);
        Assert.Equal(DataGridSortDirection.None, grid.Sort.Direction);
        Assert.Equal(100, grid.ColumnWidths[0]);
        Assert.Equal(0, sorts);
        Assert.Equal(0, widths);
        Assert.Equal(before.Nodes[0].Id, grid.AutomationId);
        fail = false;
        surface.UpdateLayout();
        Assert.Equal(new[] { "0", "1", "2" }, grid.Keys);
        Assert.Equal(180, grid.ColumnWidths[0]);
        Assert.Equal(1, sorts);
        Assert.Equal(1, widths);
    }

    /// <summary>Fixed template values compile but cannot share a mounted or parented subtree.</summary>
    [Fact]
    public void ConstantTemplatesRespectExclusiveOwnership()
    {
        Lumyte.UI.Compose.Definitions.Widget fixedCell = SizedBox();
        Compose.Definitions.DataGrid<int> declaration = DataGrid<int>(items: new[] { 0, 1 }, keySelector: Key, columns: Columns())[
            DataGridFactory<int>().Header()[Header], DataGridFactory<int>().Cell()[fixedCell]];
        using var surface = new UiSurface(declaration);
        surface.SetViewport(new(240, 100));
        Assert.Throws<InvalidOperationException>(surface.UpdateLayout);
    }

    /// <summary>The comparer cannot queue grid mutation before the surface adopts the projection.</summary>
    [Fact]
    public void ComparisonReentryRejectsBeforeChangingRequests()
    {
        DataGridElement<int>? grid = null;
        bool reenter = false;
        var comparer = Comparer<int>.Create((left, right) =>
        {
            if (reenter)
            {
                Assert.Throws<InvalidOperationException>(() => grid!.ResizeColumn("first", 180));
                Assert.Throws<InvalidOperationException>(() => grid!.CycleSort("first"));
                Assert.Throws<InvalidOperationException>(() => grid!.BringIntoView("2"));
            }

            return left.CompareTo(right);
        });
        Compose.Definitions.DataGrid<int> declaration = DataGrid<int>(items: new[] { 2, 1, 0 }, keySelector: Key, columns: new[] { new DataGridColumn<int>("first", "First", 100, comparer: comparer) })[
            DataGridFactory<int>().Header()[Header], DataGridFactory<int>().Cell()[Cell]];
        using var surface = new UiSurface(declaration);
        surface.SetViewport(new(240, 100));
        surface.UpdateLayout();
        grid = Assert.IsType<DataGridElement<int>>(surface.RootElement);
        reenter = true;
        grid.CycleSort("first");
        surface.UpdateLayout();
        Assert.Equal(100, grid.ColumnWidths[0]);
        Assert.Equal(new[] { "0", "1", "2" }, grid.Keys);
    }

    /// <summary>Splitter pointer capture changes width without activating the sort action.</summary>
    [Fact]
    public void HeaderResizeCaptureDoesNotSortAndCancelKeepsAcceptedWidth()
    {
        using var surface = new UiSurface(CreateGrid());
        surface.SetViewport(new(240, 100));
        surface.UpdateLayout();
        DataGridElement<int> grid = Assert.IsType<DataGridElement<int>>(surface.RootElement);
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(98, 10)));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Move, new(128, 10)));
        surface.UpdateLayout();
        Assert.Equal(130, grid.ColumnWidths[0]);
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Cancel, new(128, 10)));
        Assert.Equal(DataGridSortDirection.None, grid.Sort.Direction);
        Assert.Equal(130, grid.ColumnWidths[0]);
        surface.Dispatch(new UiPointerInput(2, UiPointerPhase.Down, new(10, 10)));
        surface.Dispatch(new UiPointerInput(2, UiPointerPhase.Move, new(128, 10)));
        surface.Dispatch(new UiPointerInput(2, UiPointerPhase.Up, new(128, 10)));
        surface.UpdateLayout();
        Assert.Equal(DataGridSortDirection.None, grid.Sort.Direction);
        surface.Dispatch(new UiPointerInput(3, UiPointerPhase.Down, new(128, 10)));
        surface.Dispatch(new UiPointerInput(3, UiPointerPhase.Move, new(158, 10)));
        surface.Dispatch(new UiPointerInput(3, UiPointerPhase.Up, new(158, 10)));
        surface.UpdateLayout();
        Assert.Equal(160, grid.ColumnWidths[0]);
        Assert.Equal(DataGridSortDirection.None, grid.Sort.Direction);
        surface.Dispatch(new UiPointerInput(4, UiPointerPhase.Down, new(158, 10)));
        surface.Dispatch(new UiPointerInput(4, UiPointerPhase.Up, new(178, 10)));
        surface.UpdateLayout();
        Assert.Equal(180, grid.ColumnWidths[0]);
        Assert.Equal(DataGridSortDirection.None, grid.Sort.Direction);
    }

    /// <summary>A fixed composition is legal at one exclusive target, never implicitly cloned across surfaces.</summary>
    [Fact]
    public void OneCellConstantTemplateIsLegalButCannotMountTwice()
    {
        Compose.Definitions.DataGrid<int> declaration = DataGrid<int>(items: new[] { 0 }, keySelector: Key, columns: new[] { new DataGridColumn<int>("first", "First") })[
            DataGridFactory<int>().Header()[SizedBox()], DataGridFactory<int>().Cell()[SizedBox()]];
        using var first = new UiSurface(declaration);
        first.SetViewport(new(240, 100));
        first.UpdateLayout();
        Assert.Single(first.CaptureAutomationSnapshot().Nodes, node => node.Role == UiRole.GridCell);
        Assert.Throws<InvalidOperationException>(() => new UiSurface(declaration));
    }

    /// <summary>Horizontal and vertical reveal share one prepared geometry boundary.</summary>
    [Fact]
    public void CellRevealClipsColumnsAndRollsBackBothAxesOnFactoryFailure()
    {
        bool fail = false;
        Lumyte.UI.Compose.Definitions.Widget Render(DataGridCellContext<int> cell, Compose.Definitions.DataGrid<int> owner) => fail && cell.Row >= 990 ? throw new InvalidOperationException("row") : SizedBox();
        Compose.Definitions.DataGrid<int> declaration = DataGrid<int>(items: Enumerable.Range(0, 1000).ToArray(), keySelector: Key, columns: new[] { new DataGridColumn<int>("first", "First", 160), new DataGridColumn<int>("last", "Last", 160) }, height: 100, virtualization: new UiItemVirtualization(20, 0))[
            DataGridFactory<int>().Header()[Header], DataGridFactory<int>().Cell()[Render]];
        using var surface = new UiSurface(declaration);
        surface.SetViewport(new(120, 100));
        surface.UpdateLayout();
        DataGridElement<int> grid = Assert.IsType<DataGridElement<int>>(surface.RootElement);
        ScrollViewElement horizontal = Assert.IsType<ScrollViewElement>(grid.Children[0]);
        UiAutomationSnapshot before = surface.CaptureAutomationSnapshot();
        Assert.Equal(0, horizontal.Controller.Offset);
        Assert.All(before.Nodes.Where(node => node.GridCell?.ColumnKey == "last"), node => Assert.Equal(0, node.ClippedBounds.Width));
        fail = true;
        Assert.True(grid.BringCellIntoView("999", "last"));
        Assert.Throws<InvalidOperationException>(surface.UpdateLayout);
        Assert.Equal(0, horizontal.Controller.Offset);
        Assert.Equal("0", grid.Keys[0]);
        fail = false;
        surface.UpdateLayout();
        UiAutomationNode target = Assert.Single(surface.CaptureAutomationSnapshot().Nodes, node => node.GridCell is { RowKey: "999", ColumnKey: "last" });
        Assert.True(target.ClippedBounds.Width > 0 && target.ClippedBounds.Height > 0);
        Assert.True(horizontal.Controller.Offset > 0);
    }

    /// <summary>No row factory runs while either outer viewport dimension is zero.</summary>
    /// <param name="width">The available width.</param>
    /// <param name="height">The available height.</param>
    [Theory]
    [InlineData(0, 100)]
    [InlineData(200, 0)]
    [InlineData(0, 0)]
    public void ZeroViewportDoesNotConstructCells(float width, float height)
    {
        int cells = 0;
        Lumyte.UI.Compose.Definitions.Widget Render(DataGridCellContext<int> cell, Compose.Definitions.DataGrid<int> owner)
        {
            cells++;
            return SizedBox();
        }

        Compose.Definitions.DataGrid<int> declaration = DataGrid<int>(items: Enumerable.Range(0, 1000).ToArray(), keySelector: Key, columns: Columns())[
            DataGridFactory<int>().Header()[Header], DataGridFactory<int>().Cell()[Render]];
        using var surface = new UiSurface(declaration);
        surface.SetViewport(new(width, height));
        surface.UpdateLayout();
        Assert.Equal(0, cells);
        surface.SetViewport(new(200, 100));
        surface.UpdateLayout();
        Assert.InRange(cells, 1, 20);
    }

    /// <summary>All mandatory projection state is adopted before a throwing callback, with every width notification drained once.</summary>
    [Fact]
    public void ThrowingSortNotificationDoesNotUndoWidthsOrSkipTheirCallbacks()
    {
        var changes = new List<string>();
        Compose.Definitions.DataGrid<int> declaration = DataGrid<int>(items: new[] { 2, 1, 0 }, keySelector: Key, columns: Columns(), onSortChanged: _ => throw new InvalidOperationException("notification"), onColumnWidthChanged: (key, _) => changes.Add(key))[
            DataGridFactory<int>().Header()[Header], DataGridFactory<int>().Cell()[Cell]];
        using var surface = new UiSurface(declaration);
        surface.SetViewport(new(240, 100));
        surface.UpdateLayout();
        DataGridElement<int> grid = Assert.IsType<DataGridElement<int>>(surface.RootElement);
        grid.CycleSort("first");
        grid.ResizeColumn("first", 80);
        grid.ResizeColumn("second", 90);
        Assert.Throws<InvalidOperationException>(surface.UpdateLayout);
        Assert.Equal(DataGridSortDirection.Ascending, grid.Sort.Direction);
        Assert.Equal(new[] { 80f, 90f }, grid.ColumnWidths);
        Assert.Equal(new[] { "first", "second" }, changes);
        surface.UpdateLayout();
        Assert.Equal(2, changes.Count);
    }

    /// <summary>An unknown declared selection is cleared silently and leaves the first visible cell as the keyboard entry.</summary>
    [Fact]
    public void UnknownSelectionAndHiddenSortColumnFallBackSilently()
    {
        int changes = 0;
        Compose.Definitions.DataGrid<int> declaration = DataGrid<int>(items: new[] { 1, 0 }, keySelector: Key, columns: new[] { new DataGridColumn<int>("hidden", "Hidden", comparer: Comparer<int>.Default, isVisible: false), new DataGridColumn<int>("first", "First") }, selectedKey: "missing", sort: new DataGridSort("hidden", DataGridSortDirection.Ascending), onSelectionChanged: _ => changes++)[
            DataGridFactory<int>().Header()[Header], DataGridFactory<int>().Cell()[Cell]];
        using var surface = new UiSurface(declaration);
        surface.SetViewport(new(240, 100));
        surface.UpdateLayout();
        DataGridElement<int> grid = Assert.IsType<DataGridElement<int>>(surface.RootElement);
        Assert.Null(grid.SelectedKey);
        Assert.Equal(DataGridSortDirection.None, grid.Sort.Direction);
        Assert.Equal(new[] { "1", "0" }, grid.Keys);
        Assert.Equal(0, changes);
        surface.Dispatch(new UiKeyInput(Lumyte.Input.Abstractions.Key.Tab, true));
        surface.Dispatch(new UiKeyInput(Lumyte.Input.Abstractions.Key.Tab, false));
        Assert.Equal(UiRole.Button, surface.CaptureAutomationSnapshot().Nodes.Single(node => node.IsFocused).Role);
        surface.Dispatch(new UiKeyInput(Lumyte.Input.Abstractions.Key.Tab, true));
        surface.Dispatch(new UiKeyInput(Lumyte.Input.Abstractions.Key.Tab, false));
        Assert.Equal(new UiGridCellState("1", "first", 0, 0), surface.CaptureAutomationSnapshot().Nodes.Single(node => node.IsFocused).GridCell);
    }

    /// <summary>Replacing a declaration revalidates caller rows even if its source array identity is reused.</summary>
    [Fact]
    public void ReplacementDeclarationCopiesReusedSourceBuffer()
    {
        int[] rows = [2, 1, 0];
        Compose.Definitions.DataGrid<int> Build() => DataGrid<int>(items: rows, keySelector: Key, columns: Columns())[
            DataGridFactory<int>().Header()[Header], DataGridFactory<int>().Cell()[Cell]];
        using var surface = new UiSurface(Build());
        surface.SetViewport(new(240, 100));
        surface.UpdateLayout();
        DataGridElement<int> grid = Assert.IsType<DataGridElement<int>>(surface.RootElement);
        rows[0] = 3;
        Assert.Equal(new[] { "2", "1", "0" }, grid.Keys);
        surface.Rebuild(Build());
        surface.UpdateLayout();
        Assert.Equal(new[] { "3", "1", "0" }, grid.Keys);
        rows[1] = 3;
        surface.Rebuild(Build());
        Assert.Throws<ArgumentException>(surface.UpdateLayout);
        Assert.Equal(new[] { "3", "1", "0" }, grid.Keys);
    }

    /// <summary>Both row and column intent survive an initial zero viewport without preceding cell construction.</summary>
    [Fact]
    public void CellRevealBeforeInitialViewportUsesOnlyFinalRange()
    {
        int cells = 0;
        Lumyte.UI.Compose.Definitions.Widget Render(DataGridCellContext<int> cell, Compose.Definitions.DataGrid<int> owner)
        {
            cells++;
            return SizedBox();
        }

        Compose.Definitions.DataGrid<int> declaration = DataGrid<int>(items: Enumerable.Range(0, 1000).ToArray(), keySelector: Key, columns: new[] { new DataGridColumn<int>("first", "First", 160), new DataGridColumn<int>("last", "Last", 160) }, height: 100, virtualization: new UiItemVirtualization(20, 0))[
            DataGridFactory<int>().Header()[Header], DataGridFactory<int>().Cell()[Render]];
        using var surface = new UiSurface(declaration);
        DataGridElement<int> grid = Assert.IsType<DataGridElement<int>>(surface.RootElement);
        Assert.True(grid.BringCellIntoView("999", "last"));
        surface.UpdateLayout();
        Assert.Equal(0, cells);
        surface.SetViewport(new(120, 100));
        surface.UpdateLayout();
        UiAutomationNode target = Assert.Single(surface.CaptureAutomationSnapshot().Nodes, node => node.GridCell is { RowKey: "999", ColumnKey: "last" });
        Assert.True(target.ClippedBounds.Width > 0 && target.ClippedBounds.Height > 0);
        Assert.InRange(cells, 1, 12);
    }

    /// <summary>Immutable column construction rejects nonfinite and nonpositive geometry.</summary>
    /// <param name="width">The invalid initial width.</param>
    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(0)]
    [InlineData(-1)]
    public void InvalidColumnWidthIsRejected(float width) => Assert.Throws<ArgumentOutOfRangeException>(() => new DataGridColumn<int>("value", "Value", width));

    /// <summary>Duplicate hidden columns and unrepresentable row or column extents reject before templates.</summary>
    [Fact]
    public void DuplicateColumnsAndOverflowRejectBeforeFactories()
    {
        int factories = 0;
        Lumyte.UI.Compose.Definitions.Widget Render(DataGridCellContext<int> cell, Compose.Definitions.DataGrid<int> owner)
        {
            factories++;
            return SizedBox();
        }

        Compose.Definitions.DataGrid<int> Build(DataGridColumn<int>[] columns, UiItemVirtualization? virtualization = null) => DataGrid<int>(items: new[] { 0, 1 }, keySelector: Key, columns: columns, virtualization: virtualization ?? new UiItemVirtualization(20))[
            DataGridFactory<int>().Header()[Header], DataGridFactory<int>().Cell()[Render]];
        Assert.Throws<ArgumentException>(() => new UiSurface(Build([new("same", "One"), new("same", "Two", isVisible: false)])));
        Assert.Throws<ArgumentException>(() => new UiSurface(Build([new("one", "One", float.MaxValue, 1, float.MaxValue), new("two", "Two", float.MaxValue, 1, float.MaxValue)])));
        Assert.Throws<ArgumentOutOfRangeException>(() => new UiSurface(Build(Columns(), new UiItemVirtualization(float.MaxValue))));
        Assert.Equal(0, factories);
    }

    /// <summary>An empty model builds no cells and keeps selection and ordering truthful.</summary>
    [Fact]
    public void EmptyRowsHaveNoCellRealizations()
    {
        int cells = 0;
        Lumyte.UI.Compose.Definitions.Widget Render(DataGridCellContext<int> cell, Compose.Definitions.DataGrid<int> owner)
        {
            cells++;
            return SizedBox();
        }

        Compose.Definitions.DataGrid<int> declaration = DataGrid<int>(items: Array.Empty<int>(), keySelector: Key, columns: Columns(), selectedKey: "missing")[
            DataGridFactory<int>().Header()[Header], DataGridFactory<int>().Cell()[Render]];
        using var surface = new UiSurface(declaration);
        surface.SetViewport(new(240, 100));
        surface.UpdateLayout();
        DataGridElement<int> grid = Assert.IsType<DataGridElement<int>>(surface.RootElement);
        Assert.Empty(grid.Keys);
        Assert.Null(grid.SelectedKey);
        Assert.Equal(0, cells);
        Assert.DoesNotContain(surface.CaptureAutomationSnapshot().Nodes, node => node.GridCell is not null);
    }

    /// <summary>Row buffers and column settings are copied before a key selector can alter the caller's buffers.</summary>
    [Fact]
    public void CallerBuffersAreCopiedBeforeApplicationCallbacks()
    {
        int[] rows = [0, 1];
        DataGridColumn<int>[] columns = [new("original", "Original", 100)];
        var realized = new List<int>();
        string SelectKey(int value)
        {
            rows[1] = 999;
            columns[0] = new("replacement", "Replacement", 200);
            return Key(value);
        }

        Lumyte.UI.Compose.Definitions.Widget Render(DataGridCellContext<int> cell, Compose.Definitions.DataGrid<int> owner)
        {
            realized.Add(cell.Row);
            Assert.Equal("original", cell.Column.Key);
            return SizedBox();
        }

        Compose.Definitions.DataGrid<int> declaration = DataGrid<int>(items: rows, keySelector: SelectKey, columns: columns)[
            DataGridFactory<int>().Header()[Header], DataGridFactory<int>().Cell()[Render]];
        using var surface = new UiSurface(declaration);
        surface.SetViewport(new(240, 100));
        surface.UpdateLayout();
        DataGridElement<int> grid = Assert.IsType<DataGridElement<int>>(surface.RootElement);
        Assert.Equal(new[] { "0", "1" }, grid.Keys);
        Assert.Equal(new[] { 0, 1 }, realized);
        Assert.Equal(100, grid.ColumnWidths[0]);
    }

    /// <summary>Shared slot graphs never share accepted widths, ordering or reconstructed receiving declarations.</summary>
    [Fact]
    public void SharedDeclarationKeepsSurfaceStateIndependentAcrossSignalReplay()
    {
        var height = Signal.Create<float?>(100);
        var owners = new List<Compose.Definitions.DataGrid<int>>();
        Lumyte.UI.Compose.Definitions.Widget Render(DataGridCellContext<int> cell, Compose.Definitions.DataGrid<int> owner)
        {
            owners.Add(owner);
            return SizedBox();
        }

        Compose.Definitions.DataGrid<int> declaration = DataGrid<int>(items: new[] { 2, 1, 0 }, keySelector: Key, columns: Columns(), height: height)[
            DataGridFactory<int>().Header()[Header], DataGridFactory<int>().Cell()[Render]];
        using var first = new UiSurface(declaration);
        using var second = new UiSurface(declaration);
        first.SetViewport(new(240, 120));
        second.SetViewport(new(240, 120));
        first.UpdateLayout();
        second.UpdateLayout();
        DataGridElement<int> a = Assert.IsType<DataGridElement<int>>(first.RootElement);
        DataGridElement<int> b = Assert.IsType<DataGridElement<int>>(second.RootElement);
        a.CycleSort("first");
        a.ResizeColumn("first", 80);
        first.UpdateLayout();
        Assert.Equal(DataGridSortDirection.None, b.Sort.Direction);
        Assert.Equal(100, b.ColumnWidths[0]);
        Assert.Equal(DataGridSortDirection.None, declaration.Sort.Direction);
        Assert.Equal(100, declaration.Columns[0].Width);
        height.Value = 120;
        first.UpdateLayout();
        Assert.Same(a.Widget, owners.Last());
        second.UpdateLayout();
        Assert.Same(b.Widget, owners.Last());
        Assert.NotSame(a.Widget, b.Widget);
        Assert.Equal(DataGridSortDirection.Ascending, a.Sort.Direction);
        Assert.Equal(80, a.ColumnWidths[0]);
        Assert.Equal(DataGridSortDirection.None, b.Sort.Direction);
        Assert.Equal(100, b.ColumnWidths[0]);
    }

    /// <summary>A mixed resize/reveal request uses proposed widths rather than old accepted geometry.</summary>
    [Fact]
    public void CellRevealUsesProposedColumnWidthsInTheSameAtomicPass()
    {
        using var surface = new UiSurface(CreateGrid());
        surface.SetViewport(new(120, 100));
        surface.UpdateLayout();
        DataGridElement<int> grid = Assert.IsType<DataGridElement<int>>(surface.RootElement);
        grid.ResizeColumn("first", 200);
        Assert.True(grid.BringCellIntoView("999", "second"));
        surface.UpdateLayout();
        UiAutomationNode cell = Assert.Single(surface.CaptureAutomationSnapshot().Nodes, node => node.GridCell is { RowKey: "999", ColumnKey: "second" });
        ScrollController horizontal = Assert.IsType<ScrollViewElement>(grid.Children[0]).Controller;
        Assert.True(cell.ClippedBounds.Width == 100, $"cell={cell.Bounds}; clip={cell.ClippedBounds}; offset={horizontal.Offset}; extent={horizontal.Extent}; viewport={horizontal.Viewport}; widths={string.Join(",", grid.ColumnWidths)}");
        Assert.True(cell.ClippedBounds.Height > 0);
        Assert.Equal(200, grid.ColumnWidths[0]);
    }

    /// <summary>A pending G0 position request cannot replace a signal-prepared G1 model or its template owner.</summary>
    [Fact]
    public void SignalReplacementAndOldCellRevealNeverRestoreThePreviousModel()
    {
        var rows = Signal.Create<IReadOnlyList<int>>(new[] { 2, 1, 0 });
        var owners = new List<Compose.Definitions.DataGrid<int>>();
        var values = new List<int>();
        Lumyte.UI.Compose.Definitions.Widget Render(DataGridCellContext<int> cell, Compose.Definitions.DataGrid<int> owner)
        {
            owners.Add(owner);
            values.Add(cell.Row);
            return SizedBox();
        }

        Compose.Definitions.DataGrid<int> declaration = DataGrid<int>(items: rows, keySelector: Key, columns: Columns(), height: 100)[
            DataGridFactory<int>().Header()[Header], DataGridFactory<int>().Cell()[Render]];
        using var surface = new UiSurface(declaration);
        surface.SetViewport(new(120, 100));
        surface.UpdateLayout();
        DataGridElement<int> grid = Assert.IsType<DataGridElement<int>>(surface.RootElement);
        owners.Clear();
        values.Clear();
        Assert.True(grid.BringCellIntoView("2", "second"));
        rows.Value = new[] { 10, 11 };
        surface.UpdateLayout();
        Assert.Equal(new[] { "10", "11" }, grid.Keys);
        Assert.NotSame(declaration, grid.Widget);
        Assert.NotEmpty(owners);
        Assert.Same(grid.Widget, owners.Last());
        Assert.All(owners, owner => Assert.Equal(new[] { 10, 11 }, owner.Items));
        Assert.All(values, value => Assert.Contains(value, new[] { 10, 11 }));
        Assert.DoesNotContain(surface.CaptureAutomationSnapshot().Nodes, node => node.GridCell?.RowKey == "2");
    }

    /// <summary>The reused C9 queue rejects a replaced model before acting on its old row index.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task QueuedRevealRejectsReplacedGridGenerationAsync()
    {
        Compose.Definitions.DataGrid<int> Build(int[] rows) => DataGrid<int>(items: rows, keySelector: Key, columns: Columns(), height: 100)[
            DataGridFactory<int>().Header()[Header], DataGridFactory<int>().Cell()[Cell]];
        using var surface = new UiSurface(Build(Enumerable.Range(0, 100).ToArray()));
        surface.SetViewport(new(240, 100));
        surface.UpdateLayout();
        DataGridElement<int> grid = Assert.IsType<DataGridElement<int>>(surface.RootElement);
        var dispatcher = new QueuedDispatcher();
        Task pending = grid.BringIntoViewAsync(dispatcher, "99").AsTask();
        surface.Rebuild(Build(new[] { 99 }.Concat(Enumerable.Range(0, 99)).ToArray()));
        surface.UpdateLayout();
        ScrollController controller = Assert.IsType<ScrollViewElement>(grid.Children[0].Children[0].Children[1].Children[0]).Controller;
        float before = controller.Offset;
        dispatcher.Drain();
        await Assert.ThrowsAsync<InvalidOperationException>(() => pending);
        Assert.Equal(before, controller.Offset);
    }

    /// <summary>A queued owner callback cannot mutate the old viewport if dispatched from inside a comparison.</summary>
    /// <returns>The asynchronous check.</returns>
    [Fact]
    public async Task QueuedRevealReentryRejectsBeforeViewportMutationAsync()
    {
        var dispatcher = new QueuedDispatcher();
        bool drain = false;
        var comparer = Comparer<int>.Create((left, right) =>
        {
            if (drain)
            {
                dispatcher.Drain();
            }

            return left.CompareTo(right);
        });
        Compose.Definitions.DataGrid<int> declaration = DataGrid<int>(items: Enumerable.Range(0, 100).ToArray(), keySelector: Key, columns: new[] { new DataGridColumn<int>("first", "First", comparer: comparer) }, height: 100)[
            DataGridFactory<int>().Header()[Header], DataGridFactory<int>().Cell()[Cell]];
        using var surface = new UiSurface(declaration);
        surface.SetViewport(new(240, 100));
        surface.UpdateLayout();
        DataGridElement<int> grid = Assert.IsType<DataGridElement<int>>(surface.RootElement);
        Task pending = grid.BringIntoViewAsync(dispatcher, "99").AsTask();
        drain = true;
        grid.CycleSort("first");
        surface.UpdateLayout();
        await Assert.ThrowsAsync<InvalidOperationException>(() => pending);
        Assert.DoesNotContain(surface.CaptureAutomationSnapshot().Nodes, node => node.GridCell?.RowKey == "99");
        Assert.Equal(DataGridSortDirection.Ascending, grid.Sort.Direction);
    }

    /// <summary>A cell factory signal refresh restores the actual cell key rather than its passive row wrapper.</summary>
    [Fact]
    public void CellContentSignalKeepsFocusedCellAndAcceptedOrdering()
    {
        var content = Signal.Create<float?>(10);
        Lumyte.UI.Compose.Definitions.Widget Render(DataGridCellContext<int> cell, Compose.Definitions.DataGrid<int> owner)
        {
            bool initial = content.Value == 10;
            return initial ? SizedBox(height: 10) : Rectangle(fill: new Lumyte.Mathematics.Colors.SRgba<float>(1, 0, 0, 1), height: 12);
        }

        Compose.Definitions.DataGrid<int> declaration = DataGrid<int>(items: new[] { 2, 1, 0 }, keySelector: Key, columns: Columns(), height: 100)[
            DataGridFactory<int>().Header()[Header], DataGridFactory<int>().Cell()[Render]];
        using var surface = new UiSurface(declaration);
        surface.SetViewport(new(240, 100));
        surface.UpdateLayout();
        DataGridElement<int> grid = Assert.IsType<DataGridElement<int>>(surface.RootElement);
        grid.CycleSort("first");
        surface.UpdateLayout();
        UiAutomationNode before = Assert.Single(surface.CaptureAutomationSnapshot().Nodes, node => node.GridCell is { RowKey: "1", ColumnKey: "second" });
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(before.ActionableBounds.X + 5, before.ActionableBounds.Y + 5)));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(before.ActionableBounds.X + 5, before.ActionableBounds.Y + 5)));
        content.Value = 12;
        surface.UpdateLayout();
        UiAutomationNode focused = Assert.Single(surface.CaptureAutomationSnapshot().Nodes, node => node.IsFocused);
        Assert.Equal(new UiGridCellState("1", "second", 1, 1), focused.GridCell);
        Assert.IsType<Lumyte.UI.Compose.Definitions.Rectangle>(surface.FindElement(focused.Id)!.Children[0].Children[0].Children[0].Widget);
        Assert.Equal("1", grid.SelectedKey);
        Assert.Equal(DataGridSortDirection.Ascending, grid.Sort.Direction);
    }

    /// <summary>Page navigation restores focus after a target cell is first materialized offscreen.</summary>
    [Fact]
    public void PageNavigationFocusesTheNewlyMaterializedCell()
    {
        using var surface = new UiSurface(CreateGrid());
        surface.SetViewport(new(240, 100));
        surface.UpdateLayout();
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Down, new(5, 33)));
        surface.Dispatch(new UiPointerInput(1, UiPointerPhase.Up, new(5, 33)));
        for (int page = 0; page < 4; page++)
        {
            surface.Dispatch(new UiKeyInput(Lumyte.Input.Abstractions.Key.PageDown, true));
            surface.Dispatch(new UiKeyInput(Lumyte.Input.Abstractions.Key.PageDown, false));
            surface.UpdateLayout();
        }

        UiAutomationNode focused = Assert.Single(surface.CaptureAutomationSnapshot().Nodes, node => node.IsFocused);
        Assert.Equal(new UiGridCellState("12", "first", 12, 0), focused.GridCell);
        Assert.Equal("0", Assert.IsType<DataGridElement<int>>(surface.RootElement).SelectedKey);
    }

    /// <summary>Pages follow adopted constraints, viewport resizing and header replay.</summary>
    /// <param name="count">The source row count.</param>
    [Theory]
    [InlineData(1000)]
    [InlineData(8)]
    public void PageNavigationUsesAcceptedBodyViewport(int count)
    {
        var headerHeight = Signal.Create(28f);
        Compose.Definitions.DataGrid<int> declaration = DataGrid<int>(items: Enumerable.Range(0, count).ToArray(), keySelector: Key, columns: Columns(), height: 100, headerHeight: headerHeight, virtualization: new UiItemVirtualization(20, 1))[
            DataGridFactory<int>().Header()[Header], DataGridFactory<int>().Cell()[Cell]];
        using var surface = new UiSurface(declaration);
        surface.SetViewport(new(240, 60));
        surface.UpdateLayout();
        UiAutomationNode first = Assert.Single(surface.CaptureAutomationSnapshot().Nodes, node => node.GridCell is { RowIndex: 0, ColumnIndex: 0 });
        Assert.True(surface.TryFocus(surface.FindElement(first.Id)));
        Move(Lumyte.Input.Abstractions.Key.PageDown, 1);
        surface.SetViewport(new(240, 100));
        surface.UpdateLayout();
        Move(Lumyte.Input.Abstractions.Key.PageDown, 4);
        surface.SetViewport(new(240, 60));
        surface.UpdateLayout();
        Move(Lumyte.Input.Abstractions.Key.PageUp, 3);
        headerHeight.Value = 8;
        surface.UpdateLayout();
        Move(Lumyte.Input.Abstractions.Key.PageDown, 5);
        headerHeight.Value = 48;
        surface.UpdateLayout();
        Move(Lumyte.Input.Abstractions.Key.PageUp, 4);

        void Move(Lumyte.Input.Abstractions.Key key, int expected)
        {
            surface.Dispatch(new UiKeyInput(key, true));
            surface.Dispatch(new UiKeyInput(key, false));
            surface.UpdateLayout();
            UiAutomationNode focused = Assert.Single(surface.CaptureAutomationSnapshot().Nodes, node => node.IsFocused);
            Assert.Equal(expected, focused.GridCell?.RowIndex);
        }
    }

    /// <summary>Zero adopted body geometry remains finite during repeated page input.</summary>
    [Fact]
    public void ZeroBodyPageNavigationIsFiniteAndBounded()
    {
        using var surface = new UiSurface(CreateGrid());
        surface.SetViewport(new(240, 60));
        surface.UpdateLayout();
        UiAutomationNode first = Assert.Single(surface.CaptureAutomationSnapshot().Nodes, node => node.GridCell is { RowIndex: 0, ColumnIndex: 0 });
        Assert.True(surface.TryFocus(surface.FindElement(first.Id)));
        surface.SetViewport(new(240, 28));
        surface.UpdateLayout();
        DataGridElement<int> grid = Assert.IsType<DataGridElement<int>>(surface.RootElement);
        ScrollViewElement body = Assert.IsType<ScrollViewElement>(grid.Children[0].Children[0].Children[1].Children[0]);
        Assert.Equal(0, body.Controller.Viewport);
        for (int index = 0; index < 32; index++)
        {
            surface.Dispatch(new UiKeyInput(Lumyte.Input.Abstractions.Key.PageDown, true));
            surface.Dispatch(new UiKeyInput(Lumyte.Input.Abstractions.Key.PageDown, false));
            surface.Dispatch(new UiKeyInput(Lumyte.Input.Abstractions.Key.PageUp, true));
            surface.Dispatch(new UiKeyInput(Lumyte.Input.Abstractions.Key.PageUp, false));
            Assert.Equal(0, body.Controller.Viewport);
            Assert.True(float.IsFinite(body.Controller.Offset));
            Assert.True(float.IsFinite(body.Controller.Maximum));
        }

        surface.Dispatch(new UiKeyInput(Lumyte.Input.Abstractions.Key.PageDown, true));
        surface.Dispatch(new UiKeyInput(Lumyte.Input.Abstractions.Key.PageDown, false));
        Assert.Equal(0, body.Controller.Viewport);
        surface.SetViewport(new(240, 60));
        surface.UpdateLayout();
        UiAutomationNode focused = Assert.Single(surface.CaptureAutomationSnapshot().Nodes, node => node.IsFocused);
        Assert.Equal(1, focused.GridCell?.RowIndex);
    }

    /// <summary>A failed header/viewport proposal leaves body geometry and old cell bounds published.</summary>
    [Fact]
    public void FailedHeaderViewportReplayKeepsAcceptedBodyGeometry()
    {
        bool fail = false;
        var headerHeight = Signal.Create(28f);
        Lumyte.UI.Compose.Definitions.Widget Render(DataGridCellContext<int> cell, Compose.Definitions.DataGrid<int> owner) => fail ? throw new InvalidOperationException("cell") : SizedBox();
        Compose.Definitions.DataGrid<int> declaration = DataGrid<int>(items: Enumerable.Range(0, 1000).ToArray(), keySelector: Key, columns: Columns(), height: 100, headerHeight: headerHeight, virtualization: new UiItemVirtualization(20, 1))[
            DataGridFactory<int>().Header()[Header], DataGridFactory<int>().Cell()[Render]];
        using var surface = new UiSurface(declaration);
        surface.SetViewport(new(240, 60));
        surface.UpdateLayout();
        DataGridElement<int> grid = Assert.IsType<DataGridElement<int>>(surface.RootElement);
        ScrollViewElement body = Assert.IsType<ScrollViewElement>(grid.Children[0].Children[0].Children[1].Children[0]);
        UiAutomationNode before = Assert.Single(surface.CaptureAutomationSnapshot().Nodes, node => node.GridCell is { RowIndex: 0, ColumnIndex: 0 });
        Lumyte.Graphics.Drawing2D.Rect2D acceptedBounds = surface.FindElement(before.Id)!.Bounds;
        fail = true;
        headerHeight.Value = 8;
        surface.SetViewport(new(240, 100));
        Assert.Throws<UiStateUpdateException>(surface.UpdateLayout);
        Assert.Equal(32, body.Controller.Viewport);
        Assert.Equal(28, grid.Widget.HeaderHeight);
        Assert.Equal(acceptedBounds, surface.FindElement(before.Id)!.Bounds);
        Assert.Throws<UiStateUpdateException>(surface.UpdateLayout);
        Assert.Equal(32, body.Controller.Viewport);
    }

    /// <summary>Huge accepted pages saturate safely at row boundaries on repeated forward/backward input.</summary>
    [Fact]
    public void HugeBodyPagesDoNotOverflow()
    {
        Compose.Definitions.DataGrid<int> declaration = DataGrid<int>(items: Enumerable.Range(0, 8).ToArray(), keySelector: Key, columns: Columns(), height: float.MaxValue, virtualization: new UiItemVirtualization(20, 1))[
            DataGridFactory<int>().Header()[Header], DataGridFactory<int>().Cell()[Cell]];
        using var surface = new UiSurface(declaration);
        surface.SetViewport(new(240, float.MaxValue));
        surface.UpdateLayout();
        UiAutomationNode first = Assert.Single(surface.CaptureAutomationSnapshot().Nodes, node => node.GridCell is { RowIndex: 0, ColumnIndex: 0 });
        Assert.True(surface.TryFocus(surface.FindElement(first.Id)));
        for (int index = 0; index < 16; index++)
        {
            Move(Lumyte.Input.Abstractions.Key.PageDown, 7);
            Move(Lumyte.Input.Abstractions.Key.PageDown, 7);
            Move(Lumyte.Input.Abstractions.Key.PageUp, 0);
            Move(Lumyte.Input.Abstractions.Key.PageUp, 0);
        }

        void Move(Lumyte.Input.Abstractions.Key key, int expected)
        {
            surface.Dispatch(new UiKeyInput(key, true));
            surface.Dispatch(new UiKeyInput(key, false));
            surface.UpdateLayout();
            Assert.Equal(expected, Assert.Single(surface.CaptureAutomationSnapshot().Nodes, node => node.IsFocused).GridCell?.RowIndex);
        }
    }

    /// <summary>A same-key focused row in another collection does not pin an unrelated grid row.</summary>
    [Fact]
    public void ForeignCollectionFocusCannotPinSameKeyGridRows()
    {
        Compose.Definitions.DataGrid<int> declaration = CreateGrid();
        using var surface = new UiSurface(Stack()[declaration, ListBox<int>(items: new[] { 0 }, keySelector: Key, itemTemplate: _ => SizedBox(height: 20), height: 40, testId: "foreign")]);
        surface.SetViewport(new(240, 140));
        surface.UpdateLayout();
        UiAutomationSnapshot initial = surface.CaptureAutomationSnapshot();
        UiAutomationNode owner = Assert.Single(initial.Nodes, node => node.TestId == "foreign");
        UiAutomationNode option = Assert.Single(initial.Nodes, node => node.SelectionGroup == owner.Id && node.OptionKey == "0");
        Assert.True(surface.TryFocus(surface.FindElement(option.Id)));
        surface.UpdateLayout();
        DataGridElement<int> grid = Assert.IsType<DataGridElement<int>>(surface.RootElement.Children[0]);
        Assert.True(grid.BringIntoView("999"));
        surface.UpdateLayout();
        UiAutomationSnapshot revealed = surface.CaptureAutomationSnapshot();
        Assert.Equal(option.Id, Assert.Single(revealed.Nodes, node => node.IsFocused).Id);
        Assert.DoesNotContain(revealed.Nodes, node => node.GridCell?.RowKey == "0");
        Assert.Contains(revealed.Nodes, node => node.GridCell?.RowKey == "999");
    }

    /// <summary>Header function reads update only their owned display while the explicit name and widths remain stable.</summary>
    [Fact]
    public void HeaderTemplateSignalUpdatesOwnedDisplayWithoutInferringNames()
    {
        var mode = Signal.Create(false);
        Lumyte.UI.Compose.Definitions.Widget RenderHeader(DataGridColumn<int> column, Compose.Definitions.DataGrid<int> owner)
        {
            bool filled = mode.Value;
            return filled ? Rectangle(fill: new Lumyte.Mathematics.Colors.SRgba<float>(1, 0, 0, 1)) : SizedBox();
        }

        Compose.Definitions.DataGrid<int> declaration = DataGrid<int>(items: Array.Empty<int>(), keySelector: Key, columns: new[] { new DataGridColumn<int>("value", "Stable name", 100) })[
            DataGridFactory<int>().Header()[RenderHeader], DataGridFactory<int>().Cell()[Cell]];
        using var surface = new UiSurface(declaration);
        surface.SetViewport(new(200, 100));
        surface.UpdateLayout();
        mode.Value = true;
        surface.UpdateLayout();
        UiAutomationNode header = Assert.Single(surface.CaptureAutomationSnapshot().Nodes, node => node.Role == UiRole.Button);
        Assert.Equal("Stable name", header.Name);
        Assert.IsType<Lumyte.UI.Compose.Definitions.Rectangle>(surface.FindElement(header.Id)!.Children[0].Children[0].Children[0].Widget);
        Assert.Equal(100, Assert.IsType<DataGridElement<int>>(surface.RootElement).ColumnWidths[0]);
    }

    private static string Key(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static DataGridColumn<int>[] Columns() => [new("first", "First", 100, comparer: Comparer<int>.Default), new("second", "Second", 100)];

    private static Lumyte.UI.Compose.Definitions.Widget Header(DataGridColumn<int> column, Compose.Definitions.DataGrid<int> owner) => SizedBox();

    private static Lumyte.UI.Compose.Definitions.Widget Cell(DataGridCellContext<int> cell, Compose.Definitions.DataGrid<int> owner) => SizedBox();

    private static Compose.Definitions.DataGrid<int> CreateGrid() => DataGrid<int>(items: Enumerable.Range(0, 1000).ToArray(), keySelector: Key, columns: Columns(), height: 100, virtualization: new UiItemVirtualization(20, 1))[
        DataGridFactory<int>().Header()[Header], DataGridFactory<int>().Cell()[Cell]];

    private sealed class QueuedDispatcher : IUiDispatcher
    {
        private readonly Queue<Action> _actions = new();

        public bool CheckAccess() => true;

        public ValueTask InvokeAsync(Action action, CancellationToken cancellationToken = default)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _actions.Enqueue(() =>
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    action();
                    completion.SetResult();
                }
                catch (Exception error)
                {
                    completion.SetException(error);
                }
            });
            return new(completion.Task);
        }

        public ValueTask<TResult> InvokeAsync<TResult>(Func<TResult> action, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        internal void Drain()
        {
            while (_actions.TryDequeue(out Action? action))
            {
                action();
            }
        }
    }
}
