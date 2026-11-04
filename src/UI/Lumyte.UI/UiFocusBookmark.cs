namespace Lumyte.UI;

/// <summary>An opaque focus location bound to one surface and attachment lifetime.</summary>
public sealed class UiFocusBookmark
{
    internal UiFocusBookmark(UiSurface surface, UiElement? target)
    {
        Surface = surface;
        Target = target;
        Identity = target?.AutomationId;
    }

    internal UiSurface Surface { get; }

    internal UiElement? Target { get; }

    internal UiAutomationId? Identity { get; }
}
