using Lumyte.Composition;

namespace Lumyte.UI;

/// <summary>Provides generated static factories for passive UI declarations.</summary>
public static partial class Compose
{
    /// <summary>Contains widget definitions and generated construction members.</summary>
    public static partial class Definitions
    {
        /// <summary>A passive, replaceable configuration with construction-time child ownership.</summary>
        public abstract partial class Widget
        {
            private float? _width;
            private float? _height;

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
            public bool IsVisible { get; set; } = true;

            private void SetDimension(ref float? field, float? value)
            {
                if (value.HasValue)
                {
                    UiLayoutMath.ValidateDimension(value.Value, nameof(value));
                }

                field = value;
            }
        }
    }
}
