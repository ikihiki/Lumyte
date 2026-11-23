using System.Globalization;
using Lumyte.Composition;

namespace Lumyte.UI.TextEditing;

/// <summary>Provides typed editor factories.</summary>
public static partial class Compose
{
    /// <summary>Contains passive editor declarations.</summary>
    public static partial class Definitions
    {
        /// <summary>A numeric editor sharing the actual TextBox document and input owner.</summary>
        [Composable]
        public partial class NumericBox : TextBox
        {
            private CultureInfo _culture = CultureInfo.InvariantCulture;

            /// <summary>Initializes a new instance of the <see cref="NumericBox"/> class.</summary>
            public NumericBox()
            {
            }

            /// <summary>Gets or sets the finite declared value, or null for empty.</summary>
            [ComposeParameter]
            public double? Value { get; set; }

            /// <summary>Gets or sets the finite inclusive minimum.</summary>
            [ComposeParameter]
            public double Min { get; set; } = -double.MaxValue;

            /// <summary>Gets or sets the finite inclusive maximum.</summary>
            [ComposeParameter]
            public double Max { get; set; } = double.MaxValue;

            /// <summary>Gets or sets an immutable copy of the explicit parsing and formatting culture.</summary>
            [ComposeParameter]
            public CultureInfo Culture
            {
                get => _culture;
                set => _culture = CultureInfo.ReadOnly((CultureInfo)(value ?? throw new ArgumentNullException(nameof(value))).Clone());
            }

            /// <summary>Gets or sets the latest accepted-value callback.</summary>
            [ComposeParameter]
            public Action<double?>? OnChanged { get; set; }

            /// <summary>Gets or sets the successful Enter or focus-loss callback.</summary>
            [ComposeParameter]
            public Action<double?>? OnCommitted { get; set; }

            /// <inheritdoc/>
            protected override UiElement CreateElement() => new NumericBoxElement(this);

            /// <inheritdoc/>
            protected override void ValidateDeclaration(UiWidgetValidationContext context)
            {
                if (!double.IsFinite(Min) || !double.IsFinite(Max) || Min > Max || (Value is { } value && (!double.IsFinite(value) || value < Min || value > Max)))
                {
                    throw new ArgumentOutOfRangeException(nameof(Value));
                }
            }

            [ComposeSlot]
            private static void Background(NumericBox target, Lumyte.UI.Compose.Definitions.Widget[] children) => target.BackgroundChildren.Initialize(children);
        }
    }
}
