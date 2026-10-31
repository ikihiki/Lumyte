using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>Connects a widget tree with caller-managed synchronization.</summary>
public sealed partial class UiSurface
{
    private sealed class RebuildCandidate(Widget root, SignalBinding? signals)
    {
        internal Widget Root { get; } = root;

        internal SignalBinding? Signals { get; private set; } = signals;

        internal bool IsApplying { get; set; }

        internal void DisposeSignals()
        {
            Signals?.Dispose();
            Signals = null;
        }

        internal SignalBinding? TakeSignals()
        {
            SignalBinding? result = Signals;
            Signals = null;
            return result;
        }
    }
}
