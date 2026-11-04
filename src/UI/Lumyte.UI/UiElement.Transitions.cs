namespace Lumyte.UI;

/// <summary>A persistent UI instance with independent configuration, topology and lifetime.</summary>
public partial class UiElement
{
    private UiPresenceEntry? _presence;
    private ulong _visualRevision;

    /// <summary>Gets the current presentation lifetime.</summary>
    public UiPresenceState PresenceState => _presence?.State ?? (Widget.IsPresent ? UiPresenceState.Visible : UiPresenceState.Hidden);

    internal UiPresenceEntry? Presence => _presence;

    internal ulong VisualRevision => _visualRevision;

    internal bool LayoutVisible => Widget.IsVisible && PresenceState != UiPresenceState.Hidden;

    internal bool TransitionCaptureSupported => SupportsTransitionCapture;

    internal bool IsTransitioning => Surface?.PageTransitioning == true || (_presence?.IsGated ?? false) || (Parent?.IsTransitioning ?? false);

    /// <summary>Gets a value indicating whether deterministic isolated capture is supported.</summary>
    protected virtual bool SupportsTransitionCapture => false;

    internal void ConnectPresence(UiSurface surface, Compose.Definitions.Widget? previous = null)
    {
        if (_presence is null && (!Widget.IsPresent || Widget.Transitions is not null || Widget.OnEntered is not null || Widget.OnExited is not null))
        {
            _presence = new(surface, this);
            if (previous is not null)
            {
                _presence.Seed(previous.IsVisible && previous.IsPresent);
            }

            surface.AddPresence(_presence);
        }
    }

    internal void DisconnectPresence(UiSurface surface)
    {
        if (_presence is not null)
        {
            surface.RemovePresence(_presence);
            _presence = null;
        }
    }

    internal IEnumerable<UiElement> EnumerateSubtree()
    {
        var stack = new Stack<UiElement>();
        stack.Push(this);
        while (stack.TryPop(out UiElement? element))
        {
            yield return element;
            for (int index = element.Children.Count - 1; index >= 0; index--)
            {
                stack.Push(element.Children[index]);
            }
        }
    }

    internal bool HasEquivalentVisualContent(UiElement other)
    {
        if (Widget.GetType() != other.Widget.GetType() || Widget.ClipToBounds != other.Widget.ClipToBounds
            || !CanReuseTransitionCapture(other.Widget) || Children.Count != other.Children.Count)
        {
            return false;
        }

        for (int index = 0; index < Children.Count; index++)
        {
            UiElement child = Children[index];
            UiElement previous = other.Children[index];
            if (child.Bounds != previous.Bounds || !child.HasEquivalentVisualContent(previous))
            {
                return false;
            }
        }

        return true;
    }

    internal void InvalidateChildVisuals() => InvalidateTransitionCapture();

    /// <summary>Compares component-specific drawing configuration for isolated capture reuse.</summary>
    /// <param name="other">The configuration in the other surface.</param>
    /// <returns>Whether this component records equivalent local content.</returns>
    protected virtual bool CanReuseTransitionCapture(Compose.Definitions.Widget other) => false;

    /// <summary>Revokes capture agreement after a custom drawing change.</summary>
    protected void InvalidateTransitionCapture()
    {
        for (UiElement? element = this; element is not null; element = element.Parent)
        {
            element._visualRevision = checked(element._visualRevision + 1);
        }

        Surface?.InvalidateVisualRevision();
    }
}
