namespace Lumyte.Composition.Tests;

/// <summary>Provides extra factory definitions for contract tests.</summary>
public static partial class TestKit
{
    /// <summary>Contains the test components.</summary>
    public static partial class Definitions
    {
        /// <summary>A custom generic callback signature.</summary>
        /// <typeparam name="T">The input type.</typeparam>
        /// <param name="value">The callback input.</param>
        /// <returns>The callback result.</returns>
        public delegate int CallbackResult<T>(T value);

        /// <summary>Provides an inherited init-only callback with a non-null declaration default.</summary>
        public abstract class CallbackBase
        {
            /// <summary>Gets or initializes an inherited callback.</summary>
            [ComposeParameter]
            public Action? OnInherited { get; init; } = static () => { };
        }

        /// <summary>Exercises direct callbacks in generic generated factories.</summary>
        /// <typeparam name="T">The callback argument type.</typeparam>
        [Composable(Factory = "TestKit")]
        public partial class CallbackItem<T> : CallbackBase
        {
            [ComposeParameter]
            private Func<T, int>? _onResult = static _ => 42;

            /// <summary>Gets the optional function callback stored in a field.</summary>
            public Func<T, int>? OnResult => _onResult;

            /// <summary>Gets or initializes a typed callback.</summary>
            [ComposeParameter]
            public Action<T>? OnValue { get; init; } = static _ => { };

            /// <summary>Gets or sets a custom delegate callback.</summary>
            [ComposeParameter]
            public CallbackResult<T>? OnCustom { get; set; }

            /// <summary>Gets or sets an additional nullable delegate whose initializer is cleared when omitted.</summary>
            [ComposeParameter]
            public Action? Handler { get; set; } = static () => { };

            /// <summary>Gets or sets an ordinary optional reference value.</summary>
            [ComposeParameter]
            public string? Label { get; set; } = "kept";

            /// <summary>Gets or sets an ordinary optional setting.</summary>
            [ComposeParameter]
            public int Count { get; set; } = 5;
        }

        /// <summary>Exercises non-nullable and required nullable delegate arguments.</summary>
        [Composable(Factory = "TestKit")]
        public partial class RequiredCallbacks
        {
            /// <summary>Gets or sets the mandatory non-nullable callback, even though it has an initializer.</summary>
            [ComposeParameter]
            public Action ZRequired { get; set; } = static () => { };

            /// <summary>Gets or initializes the required nullable callback.</summary>
            [ComposeParameter]
            public required Action? OnRequired { get; init; }

            /// <summary>Gets or sets an optional callback ordered before the required callback by name.</summary>
            [ComposeParameter]
            public Action? AOptional { get; set; }
        }

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
