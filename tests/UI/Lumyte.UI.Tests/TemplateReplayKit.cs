using Lumyte.Composition;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI.Tests;

/// <summary>Defines an independent component exercising template reconstruction.</summary>
public static partial class TemplateReplayKit
{
    /// <summary>Contains the test declaration.</summary>
    public static partial class Definitions
    {
        /// <summary>Stores an unevaluated template without adding declaration children.</summary>
        [Composable(Factory = nameof(TemplateReplayKit))]
        public partial class Probe : Widget
        {
            /// <summary>Gets the selected template.</summary>
            public CompositionTemplate<Probe, Widget>? Template { get; private set; }

            /// <summary>Gets the number of template assignments on this declaration.</summary>
            public int AssignmentCount { get; private set; }

            /// <summary>Gets a value indicating whether an otherwise empty visual Slot was explicitly specified.</summary>
            public bool HasSpecifiedSlot { get; private set; }

            /// <summary>Registers non-child state used by an explicitly empty visual Slot.</summary>
            public void SpecifyEmptySlot() => RegisterSlotState(true, static (Probe next, bool specified) => next.HasSpecifiedSlot = specified);

            [ComposeSlot]
            private static void Body(Probe target, CompositionTemplate<Probe, Widget> template) =>
                target.RegisterTemplateSlot(template, static (Probe next, CompositionTemplate<Probe, Widget> value) =>
                {
                    next.Template = value;
                    next.AssignmentCount++;
                });
        }
    }
}
