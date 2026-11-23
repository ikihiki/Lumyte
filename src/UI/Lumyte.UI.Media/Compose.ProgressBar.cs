using Lumyte.Composition;
using Lumyte.Mathematics.Colors;

namespace Lumyte.UI.Media;

/// <summary>Provides generated media factories.</summary>
public static partial class Compose
{
    /// <summary>Contains passive media configurations.</summary>
    public static partial class Definitions
    {
        /// <summary>A read-only finite normalized progress value, without callbacks or activation.</summary>
        [Composable]
        public partial class ProgressBar : Lumyte.UI.Compose.Definitions.Widget
        {
            /// <summary>Initializes a new instance of the <see cref="ProgressBar"/> class.</summary>
            public ProgressBar()
            {
                Role = UiRole.ProgressBar;
                TrackChildren = CreateChildren(1);
                FillChildren = CreateChildren(1);
            }

            /// <summary>Gets the optional track replacement; an explicitly empty slot hides the default track.</summary>
            public WidgetChildren TrackChildren { get; }

            /// <summary>Gets the optional fill replacement; an explicitly empty slot hides the default fill.</summary>
            public WidgetChildren FillChildren { get; }

            /// <summary>Gets or sets a finite value in the closed interval [0,1].</summary>
            [ComposeParameter]
            public double Value { get; set; }

            /// <summary>Gets or sets the explicit foreground color.</summary>
            [ComposeParameter]
            public SRgba<float> Foreground { get; set => field = MediaGeometry.UnitColor(value, nameof(Foreground)); } = new(0.2f, 0.4f, 0.8f, 1);

            /// <summary>Gets or sets the explicit track color.</summary>
            [ComposeParameter]
            public SRgba<float> Background { get; set => field = MediaGeometry.UnitColor(value, nameof(Background)); } = new(0.2f, 0.2f, 0.2f, 1);

            internal bool HasTrack { get => field || TrackChildren.IsInitialized; private set; }

            internal bool HasFill { get => field || FillChildren.IsInitialized; private set; }

            /// <inheritdoc/>
            protected override UiElement CreateElement() => new ProgressBarElement(this);

            /// <inheritdoc/>
            protected override Lumyte.UI.Compose.Definitions.Widget? RebuildDeclaration(UiWidgetRebuildContext context)
            {
                var next = (ProgressBar?)base.RebuildDeclaration(context);
                if (next is not null)
                {
                    if (HasTrack && !next.HasTrack)
                    {
                        next.RegisterSlotState(true, static (ProgressBar declaration, bool specified) => declaration.HasTrack = specified);
                    }

                    if (HasFill && !next.HasFill)
                    {
                        next.RegisterSlotState(true, static (ProgressBar declaration, bool specified) => declaration.HasFill = specified);
                    }
                }

                return next;
            }

            /// <inheritdoc/>
            protected override void ValidateDeclaration(UiWidgetValidationContext context)
            {
                if (!double.IsFinite(Value) || Value < 0 || Value > 1)
                {
                    throw new ArgumentOutOfRangeException(nameof(Value));
                }
            }

            [ComposeSlot]
            private static void Track(ProgressBar target, Lumyte.UI.Compose.Definitions.Widget[] children)
            {
                target.TrackChildren.Initialize(children);
                target.RegisterSlotState(true, static (ProgressBar next, bool specified) => next.HasTrack = specified);
            }

            [ComposeSlot]
            private static void Fill(ProgressBar target, Lumyte.UI.Compose.Definitions.Widget[] children)
            {
                target.FillChildren.Initialize(children);
                target.RegisterSlotState(true, static (ProgressBar next, bool specified) => next.HasFill = specified);
            }
        }
    }
}
