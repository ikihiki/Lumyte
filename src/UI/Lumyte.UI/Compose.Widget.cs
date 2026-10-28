namespace Lumyte.UI;

/// <summary>Provides generated static factories for retained UI widgets.</summary>
public static partial class Compose
{
    /// <summary>Contains widget definitions and generated construction members.</summary>
    public static partial class Definitions
    {
        /// <summary>A node with one parent and at most one connected surface.</summary>
        public abstract class Widget
        {
            private Widget? _parent;
            private ChildCollection? _parentCollection;
            private UiSurface? _surface;
            private bool _isMutating;

            /// <summary>Initializes a new instance of the <see cref="Widget"/> class.</summary>
            protected Widget()
            {
            }

            /// <summary>Gets the owner of this node, including a background slot owner.</summary>
            public Widget? Parent
            {
                get
                {
                    VerifyAccess();
                    return _parent;
                }
            }

            internal ChildCollection? ContentCollection { get; set; }

            internal ChildCollection? BackgroundCollection { get; set; }

            internal Widget? Owner => _parent;

            internal ChildCollection? ParentCollection => _parentCollection;

            internal UiSurface? Surface => _surface;

            internal bool IsMutating => _isMutating;

            internal void VerifyAccess() => _surface?.VerifyAccess();

            internal MutationScope BeginMutation()
            {
                _surface?.VerifyMutationAllowed();
                Widget root = this;
                while (root._parent is not null)
                {
                    root = root._parent;
                }

                if (root._isMutating)
                {
                    throw new InvalidOperationException("A widget tree mutation is already in progress.");
                }

                root._isMutating = true;
                return new MutationScope(root);
            }

            internal void SetParent(Widget? parent, ChildCollection? collection)
            {
                _parent = parent;
                _parentCollection = collection;
            }

            internal void SetSurface(UiSurface? surface) => _surface = surface;

            internal List<Widget> CollectSubtree()
            {
                var result = new List<Widget>();
                var pending = new Stack<Widget>();
                pending.Push(this);
                while (pending.TryPop(out Widget? node))
                {
                    result.Add(node);
                    PushChildren(node.ContentCollection);
                    PushChildren(node.BackgroundCollection);
                }

                return result;

                void PushChildren(ChildCollection? collection)
                {
                    if (collection is null)
                    {
                        return;
                    }

                    for (int index = collection.Items.Count - 1; index >= 0; index--)
                    {
                        pending.Push(collection.Items[index]);
                    }
                }
            }

            internal readonly struct MutationScope(Widget root) : IDisposable
            {
                public void Dispose() => root._isMutating = false;
            }
        }
    }
}
