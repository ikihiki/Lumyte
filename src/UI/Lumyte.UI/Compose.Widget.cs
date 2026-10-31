using Lumyte.Composition;

namespace Lumyte.UI;

/// <summary>Provides generated static factories for retained UI widgets.</summary>
public static partial class Compose
{
    /// <summary>Contains widget definitions and generated construction members.</summary>
    public static partial class Definitions
    {
        /// <summary>A node with one parent and at most one connected surface.</summary>
        [CompositionContentType<WidgetChildren>]
        public abstract partial class Widget
        {
            private readonly List<WidgetChildren> _childCollections = new();
            private Widget? _parent;
            private UiSurface? _surface;
            private List<Widget>? _subtree;

            /// <summary>Gets the owner of this node, regardless of the component's child property.</summary>
            public Widget? Parent => _parent;

            /// <summary>Gets or sets the stable identity within one parent's child slot; null uses position and type.</summary>
            [ComposeParameter]
            public string? Key { get; set; }

            /// <summary>Gets the revision of initialized child storage in this subtree.</summary>
            public ulong StructureRevision { get; private set; }

            internal Widget? Owner => _parent;

            internal UiSurface? Surface => _surface;

            internal IReadOnlyList<WidgetChildren> ChildCollections => _childCollections;

            internal bool CanRetainInputOnRebuild => RetainsInputOnRebuild;

            /// <summary>Gets a value indicating whether transferred component state can continue captured input.</summary>
            protected virtual bool RetainsInputOnRebuild => false;

            /// <summary>Gets the borrowed surface while this widget is connected, or null after detachment.</summary>
            protected UiSurface? ConnectedSurface => _surface;

            internal void ReconcileFrom(Widget previous) => OnReconcile(previous);

            internal void RestoreAutomationIdentity(UiAutomationId identity) => AutomationId = identity;

            internal void InvalidateStructure()
            {
                for (Widget? node = this; node is not null; node = node.Owner)
                {
                    node.StructureRevision = checked(node.StructureRevision + 1);
                    node._subtree = null;
                }
            }

            internal void SetParent(Widget? parent) => _parent = parent;

            internal void SetSurface(UiSurface? surface)
            {
                if (!ReferenceEquals(_surface, surface))
                {
                    AutomationId = surface?.AllocateAutomationId() ?? default;
                    _surface = surface;
                }
            }

            internal List<Widget> CollectSubtree()
            {
                if (_subtree is { } cached)
                {
                    return cached;
                }

                var result = new List<Widget>();
                var pending = new System.Collections.Generic.Stack<Widget>();
                pending.Push(this);
                while (pending.TryPop(out Widget? node))
                {
                    result.Add(node);
                    foreach (WidgetChildren collection in node._childCollections)
                    {
                        PushChildren(collection);
                    }
                }

                _subtree = result;
                return result;

                void PushChildren(WidgetChildren collection)
                {
                    for (int index = collection.Items.Count - 1; index >= 0; index--)
                    {
                        pending.Push(collection.Items[index]);
                    }
                }
            }

            /// <summary>Prepares component-owned state from a matched declaration without changing the previous view.</summary>
            /// <param name="previous">The previous declaration of the same concrete type and logical identity.</param>
            protected virtual void OnReconcile(Widget previous)
            {
            }

            /// <summary>Creates and registers fixed child storage owned by this component.</summary>
            /// <param name="maximumCount">The nonnegative capacity; the default accepts any array length.</param>
            /// <returns>A distinct collection for a component-defined content or slot property.</returns>
            protected WidgetChildren CreateChildren(int maximumCount = int.MaxValue)
            {
                var collection = new WidgetChildren(this, maximumCount);
                _childCollections.Add(collection);
                return collection;
            }
        }
    }
}
