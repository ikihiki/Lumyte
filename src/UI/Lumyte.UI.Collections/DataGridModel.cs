namespace Lumyte.UI.Collections;

internal sealed class DataGridModel<T>
    where T : notnull
{
    internal required Row[] Source { get; init; }

    internal required Row[] Rows { get; init; }

    internal required DataGridColumn<T>[] Columns { get; init; }

    internal required float[] Widths { get; init; }

    internal required DataGridSort Sort { get; init; }

    internal static DataGridModel<T> Create(Compose.Definitions.DataGrid<T> declaration, DataGridSort? requested = null, float[]? widths = null, Row[]? source = null, DataGridColumn<T>[]? acceptedColumns = null, Row[]? acceptedProjection = null)
    {
        ArgumentNullException.ThrowIfNull(declaration.Items);
        ArgumentNullException.ThrowIfNull(declaration.Columns);
        ArgumentNullException.ThrowIfNull(declaration.KeySelector);
        ArgumentNullException.ThrowIfNull(declaration.Virtualization);
        ArgumentNullException.ThrowIfNull(declaration.HeaderTemplate);
        ArgumentNullException.ThrowIfNull(declaration.CellTemplate);
        Func<T, string> keySelector = declaration.KeySelector;
        UiItemVirtualization virtualization = declaration.Virtualization;
        if (!float.IsFinite(declaration.HeaderHeight) || declaration.HeaderHeight <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(declaration));
        }

        if (declaration.Items.Count is < 0 or > 1000000 || declaration.Columns.Count is < 0 or > 1024)
        {
            throw new ArgumentException("A grid supports at most one million rows and 1024 declared columns.", nameof(declaration));
        }

        DataGridColumn<T>[] columns = (acceptedColumns ?? CopyColumns(declaration.Columns)).ToArray();
        T[] items = source is null ? new T[declaration.Items.Count] : [];
        for (int index = 0; index < items.Length; index++)
        {
            items[index] = declaration.Items[index];
        }

        var columnKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (DataGridColumn<T> column in columns)
        {
            ArgumentNullException.ThrowIfNull(column);
            if (!columnKeys.Add(column.Key))
            {
                throw new ArgumentException("Column keys must be ordinal unique.", nameof(declaration));
            }
        }

        float[] acceptedWidths = widths?.ToArray() ?? columns.Select(column => column.Width).ToArray();
        if (acceptedWidths.Length != columns.Length || acceptedWidths.Where((width, index) => !float.IsFinite(width) || width < columns[index].MinimumWidth || width > columns[index].MaximumWidth).Any()
            || acceptedWidths.Sum(width => (double)width) > float.MaxValue)
        {
            throw new ArgumentException("Column widths must have a finite total within their bounds.", nameof(widths));
        }

        Row[] rows = source?.ToArray() ?? new Row[items.Length];
        if (source is null)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < items.Length; index++)
            {
                ArgumentNullException.ThrowIfNull(items[index]);
                string key = keySelector(items[index]);
                if (string.IsNullOrEmpty(key) || key.Length > 256 || !keys.Add(key))
                {
                    throw new ArgumentException("Row keys must be nonempty and ordinal unique.", nameof(declaration));
                }

                rows[index] = new(items[index], key, index);
            }
        }

        virtualization.Extent(rows.Length);
        DataGridSort sort = requested ?? declaration.Sort ?? throw new ArgumentNullException(nameof(declaration));
        if (!Enum.IsDefined(sort.Direction))
        {
            throw new ArgumentException("Unknown sort direction.", nameof(declaration));
        }

        DataGridColumn<T>? sortedColumn = columns.FirstOrDefault(column => column.Key == sort.ColumnKey && column.Comparer is not null);
        if (sort.Direction == DataGridSortDirection.None || sortedColumn is null)
        {
            sort = new(null, DataGridSortDirection.None);
        }

        Row[] projected = (acceptedProjection ?? rows).ToArray();
        if (sort.Direction != DataGridSortDirection.None && acceptedProjection is null)
        {
            IComparer<T> comparer = sortedColumn!.Comparer!;
            Array.Sort(projected, (left, right) =>
            {
                int result = sort.Direction == DataGridSortDirection.Ascending ? comparer.Compare(left.Item, right.Item) : comparer.Compare(right.Item, left.Item);
                return result == 0 ? left.SourceIndex.CompareTo(right.SourceIndex) : result;
            });
        }

        return new() { Source = rows, Rows = projected, Columns = columns, Widths = acceptedWidths, Sort = sort };
    }

    internal static DataGridColumn<T>[] CopyColumns(IReadOnlyList<DataGridColumn<T>> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        int count = source.Count;
        if (count is < 0 or > 1024)
        {
            throw new ArgumentException("A grid supports at most 1024 declared columns.", nameof(source));
        }

        var keys = new HashSet<string>(StringComparer.Ordinal);
        var columns = new List<DataGridColumn<T>>(count);
        for (int index = 0; index < count; index++)
        {
            DataGridColumn<T> column = source[index];
            ArgumentNullException.ThrowIfNull(column);
            if (!keys.Add(column.Key))
            {
                throw new ArgumentException("Declared column keys must be ordinal unique.", nameof(source));
            }

            if (column.IsVisible)
            {
                columns.Add(column);
            }
        }

        return columns.ToArray();
    }

    internal sealed record Row(T Item, string Key, int SourceIndex);
}
