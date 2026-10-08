namespace Lumyte.Composition.Tests;

/// <summary>Provides extra factory definitions for contract tests.</summary>
public static partial class TestKit
{
    /// <summary>Contains the test components.</summary>
    public static partial class Definitions
    {
        /// <summary>A component base with inherited optional settings.</summary>
        public abstract class Base
        {
            /// <summary>Gets a value indicating whether gets or initializes whether the component is enabled.</summary>
            [ComposeParameter]
            public bool Enabled { get; init; } = true;
        }

        /// <summary>A component with required and optional init-only settings.</summary>
        [Composable(Factory = "TestKit")]
        public partial class Item : Base
        {
            /// <summary>Gets or initializes required content.</summary>
            [ComposeParameter]
            public required string Content { get; init; }

            /// <summary>Gets or sets an optional count.</summary>
            [ComposeParameter]
            public int Count { get; set; } = 5;
        }
    }
}
