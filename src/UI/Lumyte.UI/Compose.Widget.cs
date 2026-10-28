using Lumyte.Composition;

namespace Lumyte.UI;

/// <summary>Provides generated static factories for retained UI widgets.</summary>
public static partial class Compose
{
    /// <summary>Contains widget definitions and generated construction members.</summary>
    public static partial class Definitions
    {
        /// <summary>A node with one parent and at most one connected surface.</summary>
        [CompositionContentType(typeof(WidgetChildren))]
        public abstract partial class Widget
        {
            private Widget? _parent;
            private UiSurface? _surface;

            /// <summary>Initializes a new instance of the <see cref="Widget"/> class.</summary>
            /// <param name="maximumChildren">The maximum number of ordinary children.</param>
            /// <param name="maximumBackgroundChildren">The maximum number of background children.</param>
            protected Widget(int maximumChildren = 0, int maximumBackgroundChildren = 0)
            {
                ContentCollection = new WidgetChildren(this, maximumChildren);
                BackgroundCollection = new WidgetChildren(this, maximumBackgroundChildren);
            }

            /// <summary>Gets the owner of this node, including a background slot owner.</summary>
            public Widget? Parent => _parent;

            internal Widget? Owner => _parent;

            internal UiSurface? Surface => _surface;

            /// <summary>Gets this widget's fixed ordinary content storage.</summary>
            protected internal WidgetChildren ContentCollection { get; }

            /// <summary>Gets this widget's fixed background storage.</summary>
            protected internal WidgetChildren BackgroundCollection { get; }

            internal void VerifyMutationAllowed() => _surface?.VerifyMutationAllowed();

            internal void SetParent(Widget? parent) => _parent = parent;

            internal void SetSurface(UiSurface? surface) => _surface = surface;

            internal List<Widget> CollectSubtree()
            {
                var result = new List<Widget>();
                var pending = new System.Collections.Generic.Stack<Widget>();
                pending.Push(this);
                while (pending.TryPop(out Widget? node))
                {
                    result.Add(node);
                    PushChildren(node.ContentCollection);
                    PushChildren(node.BackgroundCollection);
                }

                return result;

                void PushChildren(WidgetChildren collection)
                {
                    for (int index = collection.Items.Count - 1; index >= 0; index--)
                    {
                        pending.Push(collection.Items[index]);
                    }
                }
            }
        }
    }
}
