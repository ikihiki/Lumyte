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
        [ComposeArgumentAdapter(typeof(UiArgument<>))]
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

            internal Widget? Recreate(UiWidgetRebuildContext context)
            {
                Widget? next = RebuildDeclaration(context);
                if (next is not null)
                {
                    for (int index = 0; index < Math.Min(ChildCollections.Count, next.ChildCollections.Count); index++)
                    {
                        WidgetChildren before = ChildCollections[index];
                        WidgetChildren after = next.ChildCollections[index];
                        if (before.IsPortal && after.IsPortal && before.IsInitialized && !after.IsInitialized)
                        {
                            after.Initialize(before.Select(child => (UiDeclarationRecipe.Capture(child)
                                ?? throw new InvalidOperationException("Portal content requires a construction recipe.")).Build()).ToArray());
                        }
                    }
                }

                return next;
            }

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

            /// <summary>Creates declaration-only content for an independent surface.</summary>
            /// <returns>A single-assignment portal slot excluded from mounted owner children.</returns>
            protected WidgetChildren CreatePortalChildren()
            {
                var children = new WidgetChildren(this, ChildCollections.Count, int.MaxValue, true);
                (_childCollections ??= []).Add(children);
                return children;
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
