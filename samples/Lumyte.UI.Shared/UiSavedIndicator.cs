using Lumyte.Graphics.Drawing2D;

namespace Lumyte.UI.SampleSupport;

// Passive indicator configuration; callers rebuild the declaration when visibility changes.
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
}
