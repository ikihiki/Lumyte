using Lumyte.Composition;

namespace Lumyte.UI;

/// <summary>Provides generated static factories for retained UI widgets.</summary>
public static partial class Compose
{
    /// <summary>Contains widget definitions and generated construction members.</summary>
    public static partial class Definitions
    {
        /// <summary>A container owning an ordered list of ordinary children.</summary>
        [Composable]
        public partial class Panel : Widget
        {
            /// <summary>Initializes a new instance of the <see cref="Panel"/> class.</summary>
            public Panel()
            {
                ContentCollection = new ChildCollection(this, int.MaxValue);
            }

            /// <summary>Gets or sets the owned children using an immutable snapshot and atomic replacement.</summary>
            [ComposeContent]
            public IReadOnlyList<Widget> Children
            {
                get => ContentCollection!.Items;
                set => ContentCollection!.Replace(value);
            }
        }
    }
}
