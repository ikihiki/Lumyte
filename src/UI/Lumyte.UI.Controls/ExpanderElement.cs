using ExpanderWidget = Lumyte.UI.Controls.Compose.Definitions.Expander;

namespace Lumyte.UI.Controls;

/// <summary>Owns surface-local accepted expansion without changing passive declarations.</summary>
public class ExpanderElement : HeaderPanelElement<ExpanderWidget>
{
    /// <summary>Initializes a new instance of the <see cref="ExpanderElement"/> class.</summary>
    /// <param name="widget">The initial declaration.</param>
    protected internal ExpanderElement(ExpanderWidget widget)
        : base(widget)
    {
        IsExpanded = widget.IsExpanded;
    }

    /// <summary>Gets a value indicating whether content is expanded.</summary>
    public bool IsExpanded { get; private set; }

    /// <inheritdoc/>
    protected override WidgetChildren HeaderSlot => Widget.HeaderChildren;

    /// <inheritdoc/>
    protected override WidgetChildren ContentSlot => Widget.ContentChildren;

    /// <inheritdoc/>
    protected override bool ContentActive => IsExpanded;

    /// <inheritdoc/>
    protected override bool? AutomationExpanded => IsExpanded;

    /// <inheritdoc/>
    protected override UiAutomationActions AutomationActions => base.AutomationActions | UiAutomationActions.ExpandCollapse;

    /// <inheritdoc/>
    protected override UiInputResult OnInput(in UiInputContext context)
    {
        if (context.Input is UiExpandInput expand)
        {
            if (Surface is not { } surface)
            {
                return UiInputResult.StopPropagation;
            }

            if (IsExpanded != expand.IsExpanded)
            {
                long request = surface.FocusRequest;
                SetExpanded(expand.IsExpanded, request);
                if (ReferenceEquals(Surface, surface) && surface.FocusRequest == request && Widget.Focusable)
                {
                    context.RequestFocus();
                }

                Widget.OnChanged?.Invoke(expand.IsExpanded);
            }

            return UiInputResult.Handled;
        }

        return base.OnInput(in context);
    }

    /// <inheritdoc/>
    protected override Action? PrepareUpdate(ExpanderWidget next, UiElementUpdateContext context)
    {
        bool expanded = ReferenceEquals(next, Widget) ? IsExpanded : next.IsExpanded;
        long request = Surface?.FocusRequest ?? 0;
        bool restore = !expanded && Surface?.FocusedElement is { } focused && GetChildren(ContentSlot).Any(child => child.EnumerateSubtree().Contains(focused));
        return () =>
        {
            IsExpanded = expanded;
            InvalidateLayout();
            InvalidateInput();
            if (restore)
            {
                Surface?.QueueItemFocus(this, request);
            }
        };
    }

    /// <inheritdoc/>
    protected override void Activate()
    {
        bool expanded = !IsExpanded;
        SetExpanded(expanded, Surface!.FocusRequest);
        Action<bool>? callback = Widget.OnChanged;
        callback?.Invoke(expanded);
    }

    private void SetExpanded(bool expanded, long request)
    {
        if (IsExpanded == expanded)
        {
            return;
        }

        IsExpanded = expanded;
        InvalidateLayout();
        InvalidateDrawing();
        Surface?.ContentPresentationChanged(this, request);
    }
}
