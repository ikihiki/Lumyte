using System.Numerics;
using Lumyte.Composition;
using Lumyte.Graphics.Drawing2D;

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
            private float? _width;
            private float? _height;
            private bool _isVisible = true;
            private bool _isLayoutDirty = true;
            private Vector2 _size;
            private Rect2D _bounds;

            /// <summary>Gets the size committed by the last successful layout.</summary>
            public Vector2 Size => _size;

            /// <summary>Gets the committed bounds in the parent's logical coordinate space.</summary>
            public Rect2D Bounds => _bounds;

            /// <summary>Gets or sets a finite nonnegative width, or null for the parent's constraints.</summary>
            [ComposeParameter]
            public float? Width
            {
                get => _width;
                set => SetDimension(ref _width, value);
            }

            /// <summary>Gets or sets a finite nonnegative height, or null for the parent's constraints.</summary>
            [ComposeParameter]
            public float? Height
            {
                get => _height;
                set => SetDimension(ref _height, value);
            }

            /// <summary>Gets or sets a value indicating whether this subtree participates in layout.</summary>
            [ComposeParameter]
            public bool IsVisible
            {
                get => _isVisible;
                set
                {
                    VerifyMutationAllowed();
                    if (_isVisible != value)
                    {
                        _isVisible = value;
                        MarkLayoutDirty();
                    }
                }
            }

            internal bool IsLayoutDirty => _isLayoutDirty;

            internal Vector2 InvokeLayout(UiBoxConstraints constraints) => LayoutOverride(constraints);

            internal void PublishLayout(Vector2 size, Vector2 offset)
            {
                _size = size;
                _bounds = new(offset.X, offset.Y, size.X, size.Y);
                _isLayoutDirty = false;
            }

            internal void MarkLayoutDirty()
            {
                for (Widget? node = this; node is not null; node = node._parent)
                {
                    node._isLayoutDirty = true;
                }
            }

            /// <summary>Invalidates this node and its ancestors before changing custom layout state.</summary>
            protected void InvalidateLayout()
            {
                VerifyMutationAllowed();
                MarkLayoutDirty();
            }

            /// <summary>Returns a finite size within the supplied, dimension-adjusted constraints.</summary>
            /// <param name="constraints">The effective constraints for this widget.</param>
            /// <returns>The final size; the leaf implementation chooses the constrained zero size.</returns>
            protected virtual Vector2 LayoutOverride(UiBoxConstraints constraints) => constraints.Constrain(Vector2.Zero);

            /// <summary>Lays out a direct child once during the current override.</summary>
            /// <param name="child">The owned content or background child.</param>
            /// <param name="constraints">The constraints to pass down.</param>
            /// <returns>The child's size for this pass, without changing its public geometry.</returns>
            protected Vector2 LayoutChild(Widget child, UiBoxConstraints constraints) =>
                GetLayoutPass().LayoutChild(this, child, constraints);

            /// <summary>Assigns a laid out direct child's position once without changing its size.</summary>
            /// <param name="child">The previously laid out child.</param>
            /// <param name="offset">The finite parent-local offset.</param>
            protected void SetChildOffset(Widget child, Vector2 offset) =>
                GetLayoutPass().SetChildOffset(this, child, offset);

            private UiLayoutPass GetLayoutPass() => _surface?.LayoutPass
                ?? throw new InvalidOperationException("Layout helpers require an active surface layout.");

            private void SetDimension(ref float? field, float? value)
            {
                VerifyMutationAllowed();
                if (value.HasValue)
                {
                    UiLayoutMath.ValidateDimension(value.Value, nameof(value));
                }

                if (field != value)
                {
                    field = value;
                    MarkLayoutDirty();
                }
            }
        }
    }
}
