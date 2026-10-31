using Lumyte.Graphics.Drawing2D;

namespace Lumyte.UI.SampleSupport;

// A small runtime component: its own state change explicitly invalidates layout/input.
internal sealed class UiSavedIndicator : Compose.Definitions.Rectangle
{
    internal UiSavedIndicator()
    {
        TestId = "saved-indicator";
        Width = 8;
        Height = 8;
        Fill = new Color2D(0, 1, 0, 1);
        IsVisible = false;
    }

    internal void Show()
    {
        IsVisible = true;
        InvalidateLayout();
        InvalidateInput();
    }
}
