namespace Lumyte.UI.Collections;

internal sealed class DataGridRow : Lumyte.UI.Compose.Definitions.Stack
{
    internal required string RowKey { get; init; }

    internal required Func<UiAutomationId> Owner { get; init; }

    internal required Func<bool> Selected { get; init; }

    protected override UiElement CreateElement() => new RowElement(this);

    private sealed class RowElement(DataGridRow widget) : StackElement(widget)
    {
        protected override string? AutomationOptionKey => ((DataGridRow)Widget).RowKey;

        protected override UiAutomationId? AutomationSelectionGroup => ((DataGridRow)Widget).Owner();

        protected override bool? AutomationSelected => ((DataGridRow)Widget).Selected();
    }
}
