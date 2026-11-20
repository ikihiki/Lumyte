using Lumyte.Composition;
using Lumyte.Mathematics.Colors;

namespace Lumyte.UI;

/// <summary>Provides generated UI declarations.</summary>
public static partial class Compose
{
    /// <summary>Contains passive definitions.</summary>
    public static partial class Definitions
    {
        /// <summary>Divides finite bounds among ordinary children without placement wrappers.</summary>
        [Composable]
        public partial class SplitView : Panel
        {
            /// <summary>Initializes a new instance of the <see cref="SplitView"/> class.</summary>
            public SplitView()
            {
                Dividers = CreateChildren();
                ClipToBounds = true;
                Role = UiRole.SplitView;
            }

            /// <summary>Gets or sets the main axis; initially horizontal.</summary>
            [ComposeParameter]
            public UiOrientation Orientation { get; set; } = UiOrientation.Horizontal;

            /// <summary>Gets or sets the finite nonnegative visible boundary extent.</summary>
            [ComposeParameter]
            public float DividerThickness { get; set; } = 4;

            /// <summary>Gets or sets the finite positive input extent, at least DividerThickness.</summary>
            [ComposeParameter]
            public float MinimumHitSize { get; set; } = 8;

            /// <summary>Gets or sets the finite positive ordinary keyboard increment.</summary>
            [ComposeParameter]
            public float SmallStep { get; set; } = 8;

            /// <summary>Gets or sets the finite positive Shift/Control keyboard increment.</summary>
            [ComposeParameter]
            public float LargeStep { get; set; } = 40;

            /// <summary>Gets or sets the explicit boundary color.</summary>
            [ComposeParameter]
            public SRgba<float> DividerColor { get; set => field = UiColorPolicy.Unit(value, nameof(DividerColor)); } = new(0.35f, 0.4f, 0.5f, 1);

            /// <summary>Gets or sets notification after atomic acceptance of changed pane sizes.</summary>
            [ComposeParameter]
            public Action<IReadOnlyList<float>>? OnChanged { get; set; }

            /// <summary>Gets or sets notification after successful gesture completion.</summary>
            [ComposeParameter]
            public Action<IReadOnlyList<float>>? OnCommitted { get; set; }

            internal WidgetChildren Dividers { get; }

            internal override bool RequiresViewportPreparation => true;

            /// <inheritdoc/>
            protected override UiElement CreateElement() => new SplitViewElement(this);

            /// <inheritdoc/>
            protected override void ValidateDeclaration(UiWidgetValidationContext context)
            {
                C4Layout.ValidateOrientation(Orientation);
                UiLayoutMath.ValidateDimension(DividerThickness, nameof(DividerThickness));
                UiLayoutMath.ValidateDimension(MinimumHitSize, nameof(MinimumHitSize));
                UiLayoutMath.ValidateDimension(SmallStep, nameof(SmallStep));
                UiLayoutMath.ValidateDimension(LargeStep, nameof(LargeStep));
                if (MinimumHitSize <= 0 || MinimumHitSize < DividerThickness || SmallStep <= 0 || LargeStep <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(MinimumHitSize));
                }

                var keys = new HashSet<string>(StringComparer.Ordinal);
                foreach (Widget child in context.GetSlotChildren(Children))
                {
                    if (child.Key is { } key && (key.Length == 0 || !keys.Add(key)))
                    {
                        throw new InvalidOperationException("Pane keys must be nonempty and ordinal unique.");
                    }
                }
            }
        }
    }
}
