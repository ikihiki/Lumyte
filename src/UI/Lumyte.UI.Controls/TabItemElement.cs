using System.Numerics;
using TabWidget = Lumyte.UI.Controls.Compose.Definitions.TabItem;

namespace Lumyte.UI.Controls;

/// <summary>A semantic header whose retained panel follows its direct TabView selection.</summary>
public class TabItemElement : HeaderPanelElement<TabWidget>
{
    /// <summary>Initializes a new instance of the <see cref="TabItemElement"/> class.</summary>
    /// <param name="widget">The initial declaration.</param>
    protected internal TabItemElement(TabWidget widget)
        : base(widget)
    {
    }

    /// <summary>Gets a value indicating whether this panel is selected.</summary>
    public bool IsSelected => Group is { } group && string.Equals(group.SelectedKey, Widget.Key, StringComparison.Ordinal);

    internal TabViewElement? Group => Parent as TabViewElement;

    /// <inheritdoc/>
    protected override WidgetChildren HeaderSlot => Widget.HeaderChildren;

    /// <inheritdoc/>
    protected override WidgetChildren ContentSlot => Widget.ContentChildren;

    /// <inheritdoc/>
    protected override bool ContentActive => IsSelected;

    /// <inheritdoc/>
    protected override bool IsTabStop => ReferenceEquals(Group?.TabStop, this);

    /// <inheritdoc/>
    protected override bool? AutomationSelected => IsSelected;

    /// <inheritdoc/>
    protected override string? AutomationOptionKey => Widget.Key;

    /// <inheritdoc/>
    protected override UiAutomationId? AutomationSelectionGroup => Group?.AutomationId;

    internal void SelectionChanged()
    {
        InvalidateLayout();
        InvalidateDrawing();
    }

    /// <inheritdoc/>
    protected override UiInputResult OnInput(in UiInputContext context)
    {
        if (context.Input is UiFocusChangedInput { IsFocused: true })
        {
            Group?.FocusHeader(this);
        }

        UiInputResult navigation = context.IsFocused ? Group?.Navigate(this, in context) ?? UiInputResult.Unhandled : UiInputResult.Unhandled;
        return navigation == UiInputResult.Unhandled ? base.OnInput(in context) : navigation;
    }

    /// <inheritdoc/>
    protected override bool HitTestSelf(Vector2 localPosition) => OnProbePointerDown(localPosition) == UiInputResult.Handled;

    /// <inheritdoc/>
    protected override Vector2 LayoutOverride(UiBoxConstraints constraints)
    {
        TabViewElement group = Group!;
        UiElement[] tabs = group.Children.Where(child => child.Widget.IsVisible && child.PresenceState != UiPresenceState.Hidden).ToArray();
        int index = Math.Max(0, Array.IndexOf(tabs, this));
        float width = Math.Min(group.Widget.HeaderWidth, constraints.MaxWidth);
        float height = Math.Min(group.Widget.HeaderHeight, constraints.MaxHeight);
        double extent = (double)height * tabs.Length;
        if (extent > float.MaxValue)
        {
            throw new InvalidOperationException("The tab header strip exceeds finite layout coordinates.");
        }

        float stripHeight = (float)extent;
        Vector2 offset = new(0, height * index);
        SetHeaderGeometry(new(width, height), offset);
        foreach (UiElement header in GetChildren(HeaderSlot))
        {
            LayoutChild(header, UiBoxConstraints.Tight(new(width, height)));
            SetChildOffset(header, offset);
        }

        Vector2 size = new(width, stripHeight);
        if (ContentActive)
        {
            foreach (UiElement content in GetChildren(ContentSlot))
            {
                Vector2 measured = LayoutChild(content, new(0, Math.Max(0, constraints.MaxWidth - width), 0, constraints.MaxHeight));
                SetChildOffset(content, new(width, 0));
                size = new(width + measured.X, Math.Max(stripHeight, measured.Y));
            }
        }

        return constraints.Constrain(size);
    }

    /// <inheritdoc/>
    protected override void Activate() => Group?.Select(this);
}
