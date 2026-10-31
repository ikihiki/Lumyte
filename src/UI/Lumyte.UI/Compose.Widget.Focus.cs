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
            /// <summary>Gets or sets a value indicating whether this declaration participates in focus traversal.</summary>
            [ComposeParameter]
            public bool Focusable { get; set; }

            internal bool InputFocusable { get; private set; }

            internal void CollectFocusCandidates(List<Widget> candidates, object version)
            {
                if (!ReferenceEquals(InputVersion, version))
                {
                    return;
                }

                if (InputFocusable)
                {
                    candidates.Add(this);
                }

                for (int index = 0; index < _inputChildren.Count; index++)
                {
                    _inputChildren[index].CollectFocusCandidates(candidates, version);
                }
            }
        }
    }
}
