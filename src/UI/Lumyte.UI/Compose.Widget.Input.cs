using System.Numerics;
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
            private IReadOnlyList<Widget> _inputChildren = Array.Empty<Widget>();
            private UiInputBounds _inputSubtreeBounds;

            /// <summary>Gets or sets a value indicating whether this node and its descendants accept input.</summary>
            [ComposeParameter]
            public bool IsEnabled { get; set; } = true;

            /// <summary>Gets or sets a value indicating whether this subtree participates in pointer and focus traversal.</summary>
            [ComposeParameter]
            public bool HitTestVisible { get; set; } = true;

            internal object? InputVersion { get; private set; }

            internal UiInputBounds InputBounds { get; private set; }

            internal Vector2 InputOrigin { get; private set; }

            internal UiInputResult InvokeInput(in UiInputContext context) => OnInput(in context);

            internal void InvokePointerCaptureChanged(UiPointerCaptureState state) => OnPointerCaptureChanged(state);

            internal void UpdateInput(Vector2 parentOrigin, UiInputBounds ancestorClip, object version)
            {
                if (!UiInputGeometry.AcceptsInput(this))
                {
                    return;
                }

                Vector2 origin = UiInputGeometry.Origin(this, parentOrigin);
                var bounds = UiInputBounds.From(origin, Size);
                UiInputBounds clip = ClipToBounds ? ancestorClip.Intersect(bounds) : ancestorClip;
                InputVersion = version;
                InputFocusable = CanReceiveFocus;
                InputBounds = bounds.Intersect(clip);
                InputOrigin = origin;
                _inputSubtreeBounds = InputBounds;
                _inputChildren = ResolveInputChildren();
                for (int index = 0; index < _inputChildren.Count; index++)
                {
                    Widget child = _inputChildren[index];
                    child.UpdateInput(origin, clip, version);
                    if (ReferenceEquals(child.InputVersion, version))
                    {
                        _inputSubtreeBounds = _inputSubtreeBounds.Union(child._inputSubtreeBounds);
                    }
                }
            }

            internal Widget? FindInputHit(Vector2 position, object? version)
            {
                if (!ReferenceEquals(InputVersion, version) || !_inputSubtreeBounds.Contains(position))
                {
                    return null;
                }

                for (int index = _inputChildren.Count - 1; index >= 0; index--)
                {
                    Widget? hit = _inputChildren[index].FindInputHit(position, version);
                    if (hit is not null)
                    {
                        return hit;
                    }
                }

                return InputBounds.Contains(position) ? this : null;
            }

            /// <summary>Handles one logical input; return Unhandled to offer it to the owning parent.</summary>
            /// <param name="context">The synchronous delivery context; do not retain it.</param>
            /// <returns>Consumption and propagation behavior.</returns>
            protected virtual UiInputResult OnInput(in UiInputContext context) => UiInputResult.Unhandled;

            /// <summary>Observes capture acquisition, refreshed input geometry or loss after the surface changes ownership.</summary>
            /// <param name="state">The current capture state; loss is also reported before Up and cancellation.</param>
            protected virtual void OnPointerCaptureChanged(UiPointerCaptureState state)
            {
            }

            /// <summary>Returns direct input children in back-to-front order; the default includes every registered group.</summary>
            /// <returns>The children selected by this component, without duplicates.</returns>
            protected virtual IEnumerable<Widget> GetInputChildren() => ChildCollections.Count switch
            {
                0 => Array.Empty<Widget>(),
                1 => ChildCollections[0],
                _ => EnumerateInputCollections(),
            };

            /// <summary>Invalidates component-owned runtime input state; declaration setters do not call this.</summary>
            protected void InvalidateInput() => _surface?.InvalidateInput();

            private IReadOnlyList<Widget> ResolveInputChildren()
            {
                IEnumerable<Widget> children = GetInputChildren()
                    ?? throw new InvalidOperationException("Input children cannot be null.");
                if (children is WidgetChildren owned)
                {
                    if (owned.Count != 0 && !ReferenceEquals(owned[0].Owner, this))
                    {
                        throw new InvalidOperationException("Input children must be direct children.");
                    }

                    return owned;
                }

                if (children is Widget[] { Length: 0 })
                {
                    return Array.Empty<Widget>();
                }

                var snapshot = new List<Widget>();
                var included = new HashSet<Widget>(ReferenceEqualityComparer.Instance);
                foreach (Widget child in children)
                {
                    if (child is null || !ReferenceEquals(child.Owner, this) || !included.Add(child))
                    {
                        throw new InvalidOperationException("Input children must be distinct direct children.");
                    }

                    snapshot.Add(child);
                }

                return snapshot.ToArray();
            }

            private IEnumerable<Widget> EnumerateInputCollections()
            {
                foreach (WidgetChildren collection in ChildCollections)
                {
                    foreach (Widget child in collection)
                    {
                        yield return child;
                    }
                }
            }
        }
    }
}
