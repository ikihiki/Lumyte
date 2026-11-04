using Lumyte.Graphics.Drawing2D;

namespace Lumyte.UI;

/// <summary>An immutable, detached copy of one element's semantics and logical geometry.</summary>
public sealed class UiAutomationNode
{
    internal UiAutomationNode(UiAutomationId id, UiAutomationId? parentId, string? testId, string? name, UiRole role, Rect2D bounds, Rect2D clippedBounds, bool visible, bool enabled, bool hitTestVisible, bool focusable, bool focused, bool pressed, bool activePress, UiAutomationActions actions, bool transitioning = false)
    {
        Id = id;
        ParentId = parentId;
        TestId = testId;
        Name = name;
        Role = role;
        Bounds = bounds;
        ClippedBounds = clippedBounds;
        IsVisible = visible;
        IsEnabled = enabled;
        HitTestVisible = hitTestVisible;
        IsFocusable = focusable;
        IsFocused = focused;
        IsPressed = pressed;
        HasActivePress = activePress;
        Actions = actions;
        IsTransitioning = transitioning;
    }

    /// <summary>Gets the connection identity.</summary>
    public UiAutomationId Id { get; }

    /// <summary>Gets the semantic parent identity.</summary>
    public UiAutomationId? ParentId { get; }

    /// <summary>Gets the explicit test identity.</summary>
    public string? TestId { get; }

    /// <summary>Gets the explicit accessible name.</summary>
    public string? Name { get; }

    /// <summary>Gets the semantic role.</summary>
    public UiRole Role { get; }

    /// <summary>Gets unclipped bounds in surface logical coordinates.</summary>
    public Rect2D Bounds { get; }

    /// <summary>Gets bounds intersected with ancestor clips and viewport.</summary>
    public Rect2D ClippedBounds { get; }

    /// <summary>Gets a value indicating whether effective ancestor visibility, without area or occlusion checks.</summary>
    public bool IsVisible { get; }

    /// <summary>Gets a value indicating whether effective ancestor enablement.</summary>
    public bool IsEnabled { get; }

    /// <summary>Gets a value indicating whether effective pointer participation.</summary>
    public bool HitTestVisible { get; }

    /// <summary>Gets a value indicating whether eligibility for ordinary focus traversal.</summary>
    public bool IsFocusable { get; }

    /// <summary>Gets a value indicating whether current surface focus.</summary>
    public bool IsFocused { get; }

    /// <summary>Gets a value indicating whether the component pressed state.</summary>
    public bool IsPressed { get; }

    /// <summary>Gets a value indicating whether an active component pointer or key press, even outside its bounds.</summary>
    public bool HasActivePress { get; }

    /// <summary>Gets a value indicating whether an ancestor, element or page transition gates actions.</summary>
    public bool IsTransitioning { get; }

    /// <summary>Gets component capabilities independent of role.</summary>
    public UiAutomationActions Actions { get; }
}
