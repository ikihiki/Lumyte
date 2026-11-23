using Lumyte.Composition;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Collections;

/// <summary>Typed collection construction.</summary>
public static partial class Compose
{
    /// <summary>Passive collection declarations.</summary>
    public static partial class Definitions
    {
        /// <summary>A read-only keyed table with typed header and cell content.</summary>
        /// <typeparam name="T">The row type.</typeparam>
        [Composable]
        public partial class DataGrid<T> : Widget
            where T : notnull
        {
            /// <summary>Initializes a new instance of the <see cref="DataGrid{T}"/> class.</summary>
            public DataGrid()
            {
                Role = UiRole.DataGrid;
                Height = 240;
                ClipToBounds = true;
            }

            /// <summary>Gets or sets source rows, copied before callbacks.</summary>
            [ComposeParameter]
            public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();

            /// <summary>Gets or sets the required ordinal key selector.</summary>
            [ComposeParameter]
            public Func<T, string>? KeySelector { get; set; }

            /// <summary>Gets or sets immutable column settings, copied before callbacks.</summary>
            [ComposeParameter]
            public IReadOnlyList<DataGridColumn<T>> Columns { get; set; } = Array.Empty<DataGridColumn<T>>();

            /// <summary>Gets or sets declared initial selection.</summary>
            [ComposeParameter]
            public string? SelectedKey { get; set; }

            /// <summary>Gets or sets the latest selection callback.</summary>
            [ComposeParameter]
            public Action<string?>? OnSelectionChanged { get; set; }

            /// <summary>Gets or sets declared single-column ordering.</summary>
            [ComposeParameter]
            public DataGridSort Sort { get; set; } = new(null, DataGridSortDirection.None);

            /// <summary>Gets or sets the latest accepted sort callback.</summary>
            [ComposeParameter]
            public Action<DataGridSort>? OnSortChanged { get; set; }

            /// <summary>Gets or sets the latest accepted width callback.</summary>
            [ComposeParameter]
            public Action<string, float>? OnColumnWidthChanged { get; set; }

            /// <summary>Gets or sets fixed-height row virtualization.</summary>
            [ComposeParameter]
            public UiItemVirtualization Virtualization { get; set; } = new(28);

            /// <summary>Gets or sets finite positive pinned header height.</summary>
            [ComposeParameter]
            public float HeaderHeight { get; set; } = 28;

            internal CompositionTemplate<DataGridColumn<T>, DataGrid<T>, Widget>? HeaderTemplate { get; private set; }

            internal CompositionTemplate<DataGridCellContext<T>, DataGrid<T>, Widget>? CellTemplate { get; private set; }

            internal override bool RequiresViewportPreparation => true;

            /// <inheritdoc/>
            protected override bool RebuildsChildren => true;

            /// <inheritdoc/>
            protected override UiElement CreateElement() => new DataGridElement<T>(this);

            [ComposeSlot]
            private static void Header(DataGrid<T> target, CompositionTemplate<DataGridColumn<T>, DataGrid<T>, Widget> template) =>
                target.RegisterTemplateSlot(template, static (DataGrid<T> next, CompositionTemplate<DataGridColumn<T>, DataGrid<T>, Widget> value) => next.HeaderTemplate = value);

            [ComposeSlot]
            private static void Cell(DataGrid<T> target, CompositionTemplate<DataGridCellContext<T>, DataGrid<T>, Widget> template) =>
                target.RegisterTemplateSlot(template, static (DataGrid<T> next, CompositionTemplate<DataGridCellContext<T>, DataGrid<T>, Widget> value) => next.CellTemplate = value);
        }
    }
}
