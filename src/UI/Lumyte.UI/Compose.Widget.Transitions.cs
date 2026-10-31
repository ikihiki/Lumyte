using Lumyte.Composition;

namespace Lumyte.UI;

/// <summary>Provides generated static factories for retained UI widgets.</summary>
public static partial class Compose
{
    /// <summary>Contains widget definitions and generated construction members.</summary>
    public static partial class Definitions
    {
        /// <summary>A node with one parent and at most one connected surface.</summary>
        public abstract partial class Widget
        {
            private UiPresenceEntry? _presence;
            private ulong _visualRevision;

            /// <summary>Gets or initializes the optional shared identity within one navigation slot.</summary>
            [ComposeParameter]
            public UiSharedElementKey? SharedElementKey { get; init; }

            /// <summary>Gets or initializes immutable presence effects.</summary>
            [ComposeParameter]
            public UiElementTransitions? Transitions { get; init; }

            /// <summary>Gets or sets a value indicating whether the model requests presence.</summary>
            [ComposeParameter]
            public bool IsPresent { get; set; } = true;

            /// <summary>Gets the current presentation lifetime.</summary>
            public UiPresenceState PresenceState => _presence?.State ?? (IsPresent ? UiPresenceState.Visible : UiPresenceState.Hidden);

            /// <summary>Gets or sets the notification after final entry presentation.</summary>
            [ComposeParameter]
            public Action? OnEntered { get; set; }

            /// <summary>Gets or sets the notification after final exit presentation.</summary>
            [ComposeParameter]
            public Action? OnExited { get; set; }

            internal UiPresenceEntry? Presence => _presence;

            internal ulong VisualRevision => _visualRevision;

            internal bool LayoutVisible => IsVisible && PresenceState != UiPresenceState.Hidden;

            internal bool TransitionCaptureSupported => SupportsTransitionCapture;

            internal bool IsTransitioning => Surface?.PageTransitioning == true || (_presence?.IsGated ?? false) || (Parent?.IsTransitioning ?? false);

            /// <summary>Gets a value indicating whether deterministic isolated capture is supported.</summary>
            protected virtual bool SupportsTransitionCapture => false;

            internal void ConnectPresence(UiSurface surface)
            {
                if (_presence is null && (!IsPresent || Transitions is not null || OnEntered is not null || OnExited is not null))
                {
                    _presence = new(surface, this);
                }

                if (_presence is not null)
                {
                    _presence.Widget = this;
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

            internal void TransferPresence(Widget previous)
            {
                _presence = previous._presence;
                if (_presence is null && previous.Surface is { } surface && (!IsPresent || Transitions is not null || OnEntered is not null || OnExited is not null))
                {
                    _presence = new(surface, this);
                    _presence.Seed(previous.LayoutVisible);
                }

                previous._presence = null;
                if (_presence is not null)
                {
                    _presence.Widget = this;
                }
            }

            internal bool HasEquivalentVisualContent(Widget other)
            {
                if (GetType() != other.GetType() || ClipToBounds != other.ClipToBounds || !CanReuseTransitionCapture(other) || ChildCollections.Count != other.ChildCollections.Count)
                {
                    return false;
                }

                for (int slot = 0; slot < ChildCollections.Count; slot++)
                {
                    WidgetChildren current = ChildCollections[slot];
                    WidgetChildren previous = other.ChildCollections[slot];
                    if (current.IsPortal && previous.IsPortal)
                    {
                        continue;
                    }

                    if (current.Count != previous.Count || current.IsPortal != previous.IsPortal)
                    {
                        return false;
                    }

                    for (int index = 0; index < current.Count; index++)
                    {
                        if (current[index].Bounds != previous[index].Bounds || !current[index].HasEquivalentVisualContent(previous[index]))
                        {
                            return false;
                        }
                    }
                }

                return true;
            }

            internal void RetainVisualRevision(Widget previous, bool childrenSame)
            {
                _visualRevision = childrenSame && Width == previous.Width && Height == previous.Height && IsVisible == previous.IsVisible
                    && ClipToBounds == previous.ClipToBounds && SharedElementKey == previous.SharedElementKey && CanReuseTransitionCapture(previous)
                    ? previous._visualRevision : checked(previous._visualRevision + 1);
            }

            /// <summary>Revokes capture agreement after a mutable custom drawing change.</summary>
            protected void InvalidateTransitionCapture()
            {
                for (Widget? widget = this; widget is not null; widget = widget.Parent)
                {
                    widget._visualRevision = checked(widget._visualRevision + 1);
                }

                Surface?.InvalidateVisualRevision();
            }

            /// <summary>Checks component-specific drawing equivalence during declaration reconstruction.</summary>
            /// <param name="previous">The matched previous declaration.</param>
            /// <returns>Whether isolated capture content is unchanged.</returns>
            protected virtual bool CanReuseTransitionCapture(Widget previous) => false;
        }
    }
}
