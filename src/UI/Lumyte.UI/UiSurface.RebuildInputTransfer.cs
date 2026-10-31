using System.Runtime.ExceptionServices;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>Connects a widget tree with caller-managed synchronization.</summary>
public sealed partial class UiSurface
{
    private readonly struct RebuildInputTransfer
    {
        private readonly KeyValuePair<long, PointerCapture>[] _pointers;

        internal RebuildInputTransfer(UiSurface surface)
        {
            _pointers = surface._pointerCaptures.ToArray();
            surface._pointerCaptures.Clear();
        }

        internal void Restore(UiSurface surface, RebuildPlan plan)
        {
            Widget candidateRoot = surface._root;
            candidateRoot.MarkLayoutDirty();
            surface.InvalidateInput();
            List<Exception>? failures = null;
            foreach ((long id, PointerCapture capture) in _pointers)
            {
                if (!surface._isDisposed && plan.Matches.TryGetValue(capture.Target, out Widget? target) && target.CanRetainInputOnRebuild)
                {
                    surface._pointerCaptures.Add(id, new(target, capture.Position));
                    continue;
                }

                try
                {
                    surface.NotifyReleased(id, capture, true);
                }
                catch (Exception error)
                {
                    (failures ??= new()).Add(error);
                }
            }

            if (failures is { Count: 1 })
            {
                ExceptionDispatchInfo.Capture(failures[0]).Throw();
            }

            if (failures is not null)
            {
                throw new AggregateException(failures);
            }
        }
    }
}
