using Lumyte.Mathematics.Colors;

namespace Lumyte.UI.SampleSupport;

// Passive indicator configuration; callers rebuild the declaration when visibility changes.
internal sealed class UiSavedIndicator : Compose.Definitions.Rectangle
{
    internal UiSavedIndicator()
    {
        TestId = "saved-indicator";
        Width = 8;
        Height = 8;
        Fill = new SRgba<float>(0, 1, 0, 1);
        IsVisible = false;
    }
}
