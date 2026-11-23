using System.Numerics;
using Lumyte.Input.Abstractions;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Collections;

/// <summary>Owns one independently accepted table projection per mounted surface.</summary>
/// <typeparam name="T">The row type.</typeparam>
public class DataGridElement<T> : UiElement<Compose.Definitions.DataGrid<T>>, IUiGridViewport
    where T : notnull
{
    private DataGridModel<T>? _model;
    private bool _preparing;
    private bool _refreshing;
    private bool _geometryOnly;
    private Compose.Definitions.DataGrid<T>? _acceptedDeclaration;
    private DataGridSort? _requestedSort;
    private (string Key, long PositionRequest)? _requestedColumn;
    private string? _requestedRow;
    private long _rowPositionRequest;
    private float[]? _requestedWidths;
    private long _request;
    private bool _notifySort;
    private (string Key, float Width)[] _notifyWidths = [];
    private int _ownerThread;
    private ListBoxElement<DataGridModel<T>.Row>? _list;
    private DataGridSort? _declaredSort;
    private string? _declaredSelection;
    private DataGridColumn<T>[] _declaredColumns = [];
    private string? _activeRow;
    private string? _activeColumn;
    private (string Row, string Column, long FocusRequest)? _pendingFocus;

    /// <summary>Initializes a new instance of the <see cref="DataGridElement{T}"/> class.</summary>
    /// <param name="widget">The passive declaration.</param>
    protected internal DataGridElement(Compose.Definitions.DataGrid<T> widget)
        : base(widget)
    {
    }

    /// <summary>Gets detached projected row keys.</summary>
    public IReadOnlyList<string> Keys => Array.AsReadOnly(_model?.Rows.Select(row => row.Key).ToArray() ?? []);

    /// <summary>Gets accepted ordering.</summary>
    public DataGridSort Sort => _model?.Sort ?? new(null, DataGridSortDirection.None);

    /// <summary>Gets accepted row selection.</summary>
    public string? SelectedKey { get; private set; }

    /// <summary>Gets detached accepted column widths.</summary>
    public IReadOnlyList<float> ColumnWidths => Array.AsReadOnly(_model?.Widths.ToArray() ?? []);

    /// <inheritdoc/>
    protected override string? AutomationSelectedKey => SelectedKey;

    /// <inheritdoc/>
    protected override UiAutomationActions AutomationActions => UiAutomationActions.SelectOption;

    /// <summary>Reveals a copied row key using the existing bounded viewport.</summary>
    /// <param name="key">The ordinal row identity.</param>
    /// <returns>Whether the adopted row viewport accepts the reveal.</returns>
    public bool BringIntoView(string key)
    {
        RequireIdleOwner();
        bool accepted = FindList()?.BringIntoView(key) ?? false;
        if (accepted)
        {
            Surface?.SupersedePendingItemReveal(this);
        }

        return accepted;
    }

    /// <summary>Queues row and column visibility in the same bounded viewport preparation.</summary>
    /// <param name="rowKey">The adopted ordinal row identity.</param>
    /// <param name="columnKey">The visible ordinal column identity.</param>
    /// <returns>Whether both keys are available.</returns>
    public bool BringCellIntoView(string rowKey, string columnKey)
    {
        RequireIdleOwner();
        ArgumentException.ThrowIfNullOrEmpty(rowKey);
        ArgumentException.ThrowIfNullOrEmpty(columnKey);
        if (_model is null || !_model.Columns.Any(column => column.Key == columnKey) || Children.FirstOrDefault() is not ScrollViewElement horizontal || !BringIntoView(rowKey))
        {
            return false;
        }

        _requestedRow = rowKey;
        _rowPositionRequest = (FindList()?.Children.FirstOrDefault() as ScrollViewElement)?.Controller.PositionRequestVersion ?? 0;
        _requestedColumn = (columnKey, horizontal.Controller.ReservePositionRequest());
        InvalidateLayout();
        return true;
    }

    /// <summary>Queues a row reveal against the currently adopted model generation.</summary>
    /// <param name="dispatcher">The explicitly borrowed owner dispatcher.</param>
    /// <param name="key">The ordinal row identity.</param>
    /// <param name="cancellationToken">Cancels before owner execution.</param>
    /// <returns>Completion of the owner request; layout adopts the resulting range.</returns>
    public ValueTask BringIntoViewAsync(IUiDispatcher dispatcher, string key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentException.ThrowIfNullOrEmpty(key);
        RequireIdleOwner(allowOtherThread: true);
        cancellationToken.ThrowIfCancellationRequested();
        ListBoxElement<DataGridModel<T>.Row> list = Volatile.Read(ref _list) ?? throw new InvalidOperationException("The grid is not mounted.");
        return list.BringIntoViewAsync(new GuardedDispatcher(this, dispatcher), key, cancellationToken);
    }

    /// <summary>Requests a sort cycle through ordinary owner input.</summary>
    /// <param name="columnKey">The sortable ordinal column identity.</param>
    /// <returns>Whether the column exists and supports ordering.</returns>
    public bool CycleSort(string columnKey)
    {
        RequireIdleOwner();
        int index = Array.FindIndex(_model?.Columns ?? [], column => column.Key == columnKey && column.Comparer is not null);
        if (index < 0)
        {
            return false;
        }

        DataGridSort current = _requestedSort ?? Sort;
        DataGridSortDirection direction = current.ColumnKey != columnKey ? DataGridSortDirection.Ascending : current.Direction switch
        {
            DataGridSortDirection.Ascending => DataGridSortDirection.Descending,
            DataGridSortDirection.Descending => DataGridSortDirection.None,
            _ => DataGridSortDirection.Ascending,
        };
        _requestedSort = new(direction == DataGridSortDirection.None ? null : columnKey, direction);
        _request++;
        InvalidateLayout();
        return true;
    }

    /// <summary>Requests a finite clamped column width at the next atomic layout boundary.</summary>
    /// <param name="columnKey">The ordinal column identity.</param>
    /// <param name="width">The proposed finite width.</param>
    /// <returns>Whether the column exists.</returns>
    public bool ResizeColumn(string columnKey, float width)
    {
        RequireIdleOwner();
        if (!float.IsFinite(width))
        {
            throw new ArgumentOutOfRangeException(nameof(width));
        }

        int index = Array.FindIndex(_model?.Columns ?? [], column => column.Key == columnKey);
        if (index < 0)
        {
            return false;
        }

        DataGridColumn<T> column = _model!.Columns[index];
        float[] widths = (_requestedWidths ?? _model.Widths).ToArray();
        widths[index] = Math.Clamp(width, column.MinimumWidth, column.MaximumWidth);
        _requestedWidths = widths;
        _request++;
        InvalidateLayout();
        return true;
    }

    internal override bool PrepareViewportContent()
    {
        bool horizontalMoved = PrepareColumnReveal();
        if ((!horizontalMoved && _requestedSort is null && _requestedWidths is null) || (PreparationSurface?.PreparedViewportContent(this)?.PreparationState is GridPreparation staged && staged.Request == _request))
        {
            return false;
        }

        _refreshing = true;
        _geometryOnly = horizontalMoved && _requestedSort is null && _requestedWidths is null;
        try
        {
            PreparationSurface!.RefreshElementContent(this);
        }
        finally
        {
            _refreshing = false;
            _geometryOnly = false;
        }

        return true;
    }

    internal override void AcceptViewportPreparation()
    {
        if (_requestedColumn is not { } column || (PreparationChildren.FirstOrDefault() is ScrollViewElement horizontal
            && (horizontal.Controller.ProposedViewport > 0 || horizontal.Controller.PositionRequestVersion != column.PositionRequest)))
        {
            _requestedColumn = null;
            _requestedRow = null;
        }

        RestorePendingFocus(prepared: true);
    }

    /// <inheritdoc/>
    protected override Action? PrepareUpdate(Compose.Definitions.DataGrid<T> next, UiElementUpdateContext context) => () =>
    {
        AcceptViewportPreparation();
        bool notifySort = _notifySort;
        _notifySort = false;
        (string Key, float Width)[] widths = _notifyWidths;
        _notifyWidths = [];
        DataGridSort sort = Sort;
        List<Exception>? failures = null;
        if (notifySort)
        {
            try
            {
                Widget.OnSortChanged?.Invoke(sort);
            }
            catch (Exception error)
            {
                (failures ??= []).Add(error);
            }
        }

        foreach ((string key, float width) in widths)
        {
            try
            {
                Widget.OnColumnWidthChanged?.Invoke(key, width);
            }
            catch (Exception error)
            {
                (failures ??= []).Add(error);
            }
        }

        if (failures is { Count: 1 })
        {
            throw failures[0];
        }

        if (failures is { Count: > 1 })
        {
            throw new AggregateException(failures);
        }
    };

    /// <inheritdoc/>
    protected override UiElementContent? BuildContent(Widget next, UiElementBuildContext context)
    {
        if (_preparing)
        {
            throw new InvalidOperationException("Grid preparation cannot reenter.");
        }

        _preparing = true;
        try
        {
            var declaration = (Compose.Definitions.DataGrid<T>)next;
            bool refresh = _refreshing && ReferenceEquals(declaration, _acceptedDeclaration);
            if (_geometryOnly && refresh && PreparationChildren.FirstOrDefault() is { } currentContent)
            {
                return new UiElementContent(currentContent.Widget)
                {
                    PreparationState = new GridPreparation(_request, (PreparationSurface?.PreparedViewportContent(this)?.PreparationState as GridPreparation)?.Model ?? _model!),
                }.WithFreshItemOwnership(CollectionItemRoots.Select(element => element.Widget).ToArray());
            }

            DataGridColumn<T>[] columns = refresh ? _model!.Columns.ToArray() : DataGridModel<T>.CopyColumns(declaration.Columns);
            DataGridSort declaredSort = declaration.Sort ?? throw new ArgumentNullException(nameof(next));
            string? declaredSelection = declaration.SelectedKey;
            float[]? widths = refresh ? _requestedWidths ?? _model!.Widths : null;
            if (!refresh && _model is not null)
            {
                widths = columns.Select(column =>
                {
                    int previous = Array.FindIndex(_declaredColumns, oldColumn => oldColumn.Key == column.Key && oldColumn.Width == column.Width);
                    int accepted = Array.FindIndex(_model.Columns, oldColumn => oldColumn.Key == column.Key);
                    return previous >= 0 && accepted >= 0 ? Math.Clamp(_model.Widths[accepted], column.MinimumWidth, column.MaximumWidth) : column.Width;
                }).ToArray();
            }

            DataGridSort sort = refresh ? _requestedSort ?? Sort : _model is not null && Equals(declaredSort, _declaredSort) ? Sort : declaredSort;
            bool sameSource = _model is not null && ReferenceEquals(declaration, _acceptedDeclaration);
            bool sameOrdering = sameSource && Equals(sort, Sort) && columns.Length == _model!.Columns.Length
                && columns.Select((column, index) => column.Key == _model.Columns[index].Key && ReferenceEquals(column.Comparer, _model.Columns[index].Comparer)).All(value => value);
            var model = DataGridModel<T>.Create(declaration, sort, widths, refresh || sameSource ? _model!.Source : null, columns, sameOrdering ? _model!.Rows : null);
            long request = _request;
            bool notifySort = refresh && model.Sort != Sort;
            var notifyWidths = new List<(string Key, float Width)>();
            if (refresh)
            {
                for (int index = 0; index < model.Widths.Length; index++)
                {
                    if (model.Widths[index] != _model!.Widths[index])
                    {
                        notifyWidths.Add((model.Columns[index].Key, model.Widths[index]));
                    }
                }
            }

            string? selected = refresh || (_model is not null && declaredSelection == _declaredSelection) ? SelectedKey : declaredSelection;
            float width = (float)model.Widths.Sum(value => (double)value);
            var header = new Lumyte.UI.Compose.Definitions.Stack { Orientation = UiOrientation.Horizontal, Width = width, Height = declaration.HeaderHeight };
            header.Children.Initialize(model.Columns.Select((column, index) =>
            {
                var cell = new DataGridHeader
                {
                    Key = column.Key,
                    AutomationName = column.Name,
                    Width = model.Widths[index],
                    Height = declaration.HeaderHeight,
                    Sort = () => CycleSort(column.Key),
                    CanSort = column.Comparer is not null,
                    Resize = value => ResizeColumn(column.Key, value),
                    AcceptedWidth = () => _model?.Widths[Array.FindIndex(_model.Columns, value => value.Key == column.Key)] ?? model.Widths[index],
                };
                cell.Content.Initialize(new DataGridPart(() => Box(declaration.HeaderTemplate!(column, declaration), model.Widths[index], declaration.HeaderHeight)));
                return (Widget)cell;
            }).ToArray());
            var list = new Compose.Definitions.ListBox<DataGridModel<T>.Row>
            {
                Items = model.Rows,
                KeySelector = row => row.Key,
                ItemTemplate = row => CreateRow(declaration, model, row),
                SelectedKey = selected,
                RequestedRevealKey = _requestedRow,
                RevealPositionVersion = _rowPositionRequest,
                Role = UiRole.Group,
                HideSelectionSemantics = true,
                PrepareOwnedFocusRestore = PrepareCellFocusRestore,
                ItemsRealized = () => RestorePendingFocus(prepared: false),
                SelectionValue = () => SelectedKey,
                ConfigureItem = (row, item) =>
                {
                    item.Role = UiRole.None;
                    item.Focusable = false;
                    item.PassiveRow = true;
                    item.SelectionOwner = () => AutomationId;
                },
                OnSelectionChanged = key =>
                {
                    RequireIdleOwner();
                    SelectedKey = key;
                    InvalidateInput();
                    Widget.OnSelectionChanged?.Invoke(key);
                },
                Virtualization = declaration.Virtualization,
                ViewportAvailable = () => PreparedSize.X > 0 && PreparedSize.Y > declaration.HeaderHeight,
                Width = width,
                Height = null,
            };
            var stack = new DataGridPanel { Width = width, Height = declaration.Height, HeaderHeight = declaration.HeaderHeight };
            stack.Content.Initialize([header, list]);
            var horizontal = new Lumyte.UI.Compose.Definitions.ScrollView { Orientation = UiOrientation.Horizontal, Height = declaration.Height, DeferredPosition = true };
            horizontal.Children.Initialize([stack]);
            return new UiElementContent(horizontal, () =>
            {
                _model = model;
                _acceptedDeclaration = declaration;
                Volatile.Write(ref _list, FindList());
                _declaredSort = declaredSort;
                _declaredSelection = declaredSelection;
                _declaredColumns = columns;
                SelectedKey = model.Rows.Any(row => row.Key == selected) ? selected : null;
                _ownerThread = Environment.CurrentManagedThreadId;
                _activeRow = model.Rows.Any(row => row.Key == _activeRow) ? _activeRow : SelectedKey ?? model.Rows.FirstOrDefault()?.Key;
                _activeColumn = model.Columns.Any(column => column.Key == _activeColumn) ? _activeColumn : model.Columns.FirstOrDefault()?.Key;
                _notifySort |= notifySort;
                _notifyWidths = notifyWidths.ToArray();
                if (_request == request)
                {
                    _requestedSort = null;
                    _requestedWidths = null;
                }
            })
            {
                PreparationState = new GridPreparation(request, model),
            }.WithFreshItemOwnership(header);
        }
        finally
        {
            _preparing = false;
        }
    }

    /// <inheritdoc/>
    protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
    {
        Vector2 size = LayoutChild(Children[0], constraints);
        SetChildOffset(Children[0], Vector2.Zero);
        return constraints.Constrain(size);
    }

    private static Widget Box(Widget content, float width, float height)
    {
        ArgumentNullException.ThrowIfNull(content);
        content.ValidateFreshItemRoot();
        var box = new Lumyte.UI.Compose.Definitions.SizedBox { Width = width, Height = height, ClipToBounds = true };
        box.Children.Initialize([content]);
        return box;
    }

    private Widget CreateRow(Compose.Definitions.DataGrid<T> declaration, DataGridModel<T> model, DataGridModel<T>.Row row)
    {
        int rowIndex = Array.FindIndex(model.Rows, candidate => string.Equals(candidate.Key, row.Key, StringComparison.Ordinal));
        var cells = new Widget[model.Columns.Length];
        for (int index = 0; index < cells.Length; index++)
        {
            DataGridColumn<T> column = model.Columns[index];
            var cell = new DataGridCell
            {
                Key = column.Key,
                AutomationName = column.Name,
                Width = model.Widths[index],
                Height = declaration.Virtualization.ItemExtent,
                State = new(row.Key, column.Key, rowIndex, index),
                Activate = () => SelectCell(row.Key, column.Key),
                Focused = () => TrackCellFocus(row.Key, column.Key),
                Selected = () => SelectedKey == row.Key,
                TabStop = () => IsCellTabStop(row.Key, column.Key),
                SelectionOwner = () => AutomationId,
                Navigate = NavigateCell,
            };
            var cellContext = new DataGridCellContext<T>(row.Item, row.Key, rowIndex, column, index);
            float cellWidth = model.Widths[index];
            float cellHeight = declaration.Virtualization.ItemExtent;
            cell.Content.Initialize(new DataGridPart(() => Box(declaration.CellTemplate!(cellContext, declaration), cellWidth, cellHeight)));
            cells[index] = cell;
        }

        var stack = new DataGridRow { Role = UiRole.GridRow, RowKey = row.Key, Owner = () => AutomationId, Selected = () => SelectedKey == row.Key, Orientation = UiOrientation.Horizontal, Height = declaration.Virtualization.ItemExtent };
        stack.Children.Initialize(cells);
        return stack;
    }

    private bool PrepareColumnReveal()
    {
        DataGridModel<T>? model = (PreparationSurface?.PreparedViewportContent(this)?.PreparationState as GridPreparation)?.Model ?? _model;
        if (_requestedColumn is not { } requested || model is null || PreparationChildren.FirstOrDefault() is not ScrollViewElement horizontal)
        {
            return false;
        }

        ScrollController controller = horizontal.Controller;
        int index = Array.FindIndex(model.Columns, column => column.Key == requested.Key);
        if (index < 0 || controller.PositionRequestVersion != requested.PositionRequest || controller.ProposedViewport <= 0)
        {
            return false;
        }

        double start = model.Widths.Take(index).Sum(width => (double)width);
        double end = start + model.Widths[index];
        double offset = controller.ProposedOffset;
        double target = start < offset ? start : end > offset + controller.ProposedViewport ? Math.Min(start, end - controller.ProposedViewport) : offset;
        bool moved = controller.StagePositionRequest((float)target);
        if (moved)
        {
            _request++;
        }

        return moved;
    }

    private Action<bool>? PrepareCellFocusRestore(UiElement? focused, long request)
    {
        if (_pendingFocus is { } pending && pending.FocusRequest == request)
        {
            return null;
        }

        while (focused is not null && focused is not DataGridCell.CellElement)
        {
            focused = focused.Parent;
        }

        if (focused is not DataGridCell.CellElement previous || !Cells().Contains(previous))
        {
            return null;
        }

        UiGridCellState state = previous.Widget.State;
        return reveal =>
        {
            DataGridCell.CellElement? target = Cells().FirstOrDefault(cell => cell.Widget.State.RowKey == state.RowKey && cell.Widget.State.ColumnKey == state.ColumnKey)
                ?? Cells().FirstOrDefault(cell => cell.Widget.State.RowKey == state.RowKey);
            if (target is not null && Surface?.FocusRequest == request)
            {
                _activeRow = target.Widget.State.RowKey;
                _activeColumn = target.Widget.State.ColumnKey;
                Surface.QueueItemFocus(target, request, reveal);
                InvalidateInput();
            }
        };
    }

    private void RestorePendingFocus(bool prepared)
    {
        if (_pendingFocus is not { } pending)
        {
            return;
        }

        UiSurface? surface = Surface;
        if (surface is null || surface.FocusRequest != pending.FocusRequest)
        {
            _pendingFocus = null;
            return;
        }

        IEnumerable<DataGridCell.CellElement> cells = prepared ? PreparedCells() : Cells();
        DataGridCell.CellElement? target = cells.FirstOrDefault(cell => cell.Widget.State.RowKey == pending.Row && cell.Widget.State.ColumnKey == pending.Column);
        if (target is not null && ReferenceEquals(target.Surface, surface))
        {
            surface.QueueItemFocus(target, pending.FocusRequest);
            _pendingFocus = null;
        }
        else if (!prepared && _model is not null && (!_model.Rows.Any(row => row.Key == pending.Row) || !_model.Columns.Any(column => column.Key == pending.Column)))
        {
            _pendingFocus = null;
        }
    }

    private IEnumerable<DataGridCell.CellElement> PreparedCells()
    {
        ListBoxElement<DataGridModel<T>.Row>? list = PreparationChildren.FirstOrDefault()?.PreparationChildren.FirstOrDefault()?.PreparationChildren.OfType<ListBoxElement<DataGridModel<T>.Row>>().FirstOrDefault();
        return list?.PreparationChildren.SelectMany(scroll => scroll.PreparationChildren).SelectMany(panel => panel.PreparationChildren).OfType<CollectionItemElement>()
            .SelectMany(row => row.PreparationChildren).SelectMany(row => row.PreparationChildren).OfType<DataGridCell.CellElement>() ?? [];
    }

    private IEnumerable<DataGridCell.CellElement> Cells() => FindList()?.Options.SelectMany(row => row.Children).SelectMany(row => row.Children).OfType<DataGridCell.CellElement>() ?? [];

    private bool IsCellTabStop(string row, string column)
    {
        DataGridCell.CellElement? active = Cells().FirstOrDefault(cell => cell.Widget.State.RowKey == _activeRow && cell.Widget.State.ColumnKey == _activeColumn)
            ?? Cells().FirstOrDefault(cell => cell.Widget.State.ColumnKey == _activeColumn)
            ?? Cells().FirstOrDefault();
        return active is not null && active.Widget.State.RowKey == row && active.Widget.State.ColumnKey == column;
    }

    private void SelectCell(string row, string column)
    {
        RequireIdleOwner();
        TrackCellFocus(row, column);
        if (SelectedKey != row)
        {
            SelectedKey = row;
            Widget.OnSelectionChanged?.Invoke(row);
        }
    }

    private void TrackCellFocus(string row, string column)
    {
        _activeRow = row;
        _activeColumn = column;
        InvalidateInput();
    }

    private bool NavigateCell(DataGridCell.CellElement cell, UiKeyInput key)
    {
        RequireIdleOwner();
        if (_model is null || key.Key is not (Key.ArrowLeft or Key.ArrowRight or Key.ArrowUp or Key.ArrowDown or Key.Home or Key.End or Key.PageUp or Key.PageDown))
        {
            return false;
        }

        UiGridCellState current = cell.Widget.State;
        int row = current.RowIndex;
        int column = current.ColumnIndex;

        // Read only published vertical geometry: declarations and proposed layout can differ from the adopted body.
        // A zero/unavailable body still advances one row; input never triggers layout to obtain a viewport.
        float viewport = (FindList()?.Children.FirstOrDefault() as ScrollViewElement)?.Controller.Viewport ?? 0;
        int page = float.IsFinite(viewport) && viewport > 0
            ? (int)Math.Clamp(Math.Floor((double)viewport / Widget.Virtualization.ItemExtent), 1, int.MaxValue)
            : 1;
        switch (key.Key)
        {
            case Key.ArrowLeft: column--; break;
            case Key.ArrowRight: column++; break;
            case Key.ArrowUp: row--; break;
            case Key.ArrowDown: row++; break;
            case Key.Home: column = 0; break;
            case Key.End: column = _model.Columns.Length - 1; break;
            case Key.PageUp: row -= page; break;
            case Key.PageDown: row = (int)Math.Min(_model.Rows.Length - 1, (long)row + page); break;
        }

        row = Math.Clamp(row, 0, _model.Rows.Length - 1);
        column = Math.Clamp(column, 0, _model.Columns.Length - 1);
        _activeRow = _model.Rows[row].Key;
        _activeColumn = _model.Columns[column].Key;
        if (BringCellIntoView(_activeRow, _activeColumn) && Surface is { } surface)
        {
            _pendingFocus = (_activeRow, _activeColumn, surface.ReserveItemFocusRequest());
        }

        InvalidateInput();
        return true;
    }

    private ListBoxElement<DataGridModel<T>.Row>? FindList() => Children.FirstOrDefault()?.Children.FirstOrDefault()?.Children.OfType<ListBoxElement<DataGridModel<T>.Row>>().FirstOrDefault();

    private void RequireIdleOwner(bool allowOtherThread = false)
    {
        if (_preparing || PreparationSurface?.HasUnpublishedViewportPreparation == true || (!allowOtherThread && _ownerThread != 0 && _ownerThread != Environment.CurrentManagedThreadId))
        {
            throw new InvalidOperationException("Grid input requires an idle owner outside unpublished surface preparation.");
        }
    }

    private sealed class GuardedDispatcher(DataGridElement<T> owner, IUiDispatcher inner) : IUiDispatcher
    {
        public bool CheckAccess() => inner.CheckAccess();

        public ValueTask InvokeAsync(Action action, CancellationToken cancellationToken = default) => inner.InvokeAsync(
            () =>
            {
                owner.RequireIdleOwner();
                action();
            },
            cancellationToken);

        public ValueTask<TResult> InvokeAsync<TResult>(Func<TResult> action, CancellationToken cancellationToken = default) => inner.InvokeAsync(
            () =>
            {
                owner.RequireIdleOwner();
                return action();
            },
            cancellationToken);
    }

    private sealed record GridPreparation(long Request, DataGridModel<T> Model);
}
