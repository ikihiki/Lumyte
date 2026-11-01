using Lumyte.Composition;

namespace Lumyte.UI;

/// <summary>Provides generated static factories for UI declarations.</summary>
public static partial class Compose
{
    /// <summary>Contains declarations consumed by Composition.</summary>
    public static partial class Definitions
    {
        /// <summary>A passive, replaceable configuration for one logical UI instance.</summary>
        [CompositionContentType<WidgetChildren>]
        public abstract partial class Widget
        {
            private List<WidgetChildren>? _childCollections;

            /// <summary>Gets or initializes the identity within a parent's slot; null uses position and type.</summary>
            [ComposeParameter]
            public string? Key { get; init; }

            internal bool HasDeclarationParent { get; set; }

            internal IReadOnlyList<WidgetChildren> ChildCollections => _childCollections is { } children ? children : Array.Empty<WidgetChildren>();

            internal UiElement CreateInstance()
            {
                UiElement element = CreateElement();
                if (element is null || element.IsMounted || !ReferenceEquals(element.Widget, this))
                {
                    throw new InvalidOperationException("CreateElement must return a fresh passive instance for its declaration.");
                }

                return element;
            }

            internal void Validate(UiWidgetValidationContext context) => ValidateDeclaration(context);

            internal Widget? Recreate(UiWidgetRebuildContext context) => RebuildDeclaration(context);

            /// <summary>Creates passive instance storage for a new logical identity.</summary>
            /// <returns>A fresh element; compatible updates keep the existing element instead.</returns>
            protected virtual UiElement CreateElement() => new(this);

            /// <summary>Creates a fresh configuration for a reactive update.</summary>
            /// <param name="context">The passive child retention plan.</param>
            /// <returns>A new declaration, or null to use the enclosing construction function.</returns>
            protected virtual Widget? RebuildDeclaration(UiWidgetRebuildContext context) => CompositionFactory?.Invoke() as Widget;

            /// <summary>Checks configuration against the proposed tree, without changing live state.</summary>
            /// <param name="context">The read-only proposed configuration topology.</param>
            protected virtual void ValidateDeclaration(UiWidgetValidationContext context)
            {
            }

            /// <summary>Creates a single-assignment declaration slot owned by this widget.</summary>
            /// <param name="maximumCount">The maximum number of children.</param>
            /// <returns>New registered declaration storage.</returns>
            protected WidgetChildren CreateChildren(int maximumCount = int.MaxValue)
            {
                ArgumentOutOfRangeException.ThrowIfNegative(maximumCount);
                var children = new WidgetChildren(this, ChildCollections.Count, maximumCount);
                (_childCollections ??= []).Add(children);
                return children;
            }
        }
    }
}
