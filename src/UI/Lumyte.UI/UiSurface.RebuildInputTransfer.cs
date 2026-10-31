using System.Runtime.ExceptionServices;
using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>Connects a widget tree with caller-managed synchronization.</summary>
public sealed partial class UiSurface
{
    private readonly struct RebuildInputTransfer
    {
        private readonly KeyValuePair<long, PointerCapture>[] _pointers;
        private readonly KeyValuePair<Lumyte.Input.Abstractions.Key, Widget>[] _keys;

        internal RebuildInputTransfer(UiSurface surface)
        {
            _pointers = surface._pointerCaptures.ToArray();
            _keys = surface._keyCaptures.ToArray();
            surface._pointerCaptures.Clear();
            surface._keyCaptures.Clear();
            surface._inputReset++;
        }

        internal void Restore(UiSurface surface, RebuildPlan plan, Widget? previousFocus, Widget? nextFocus, bool layoutUnchanged)
        {
            Widget candidateRoot = surface._root;
            surface._focusedWidget = nextFocus;
            surface._focusCandidates.Clear();
            if (!layoutUnchanged)
            {
                candidateRoot.MarkLayoutDirty();
            }

            bool inputUnchanged = layoutUnchanged;
            foreach ((Widget previous, Widget current) in plan.Matches)
            {
                inputUnchanged &= current.TryReuseSignalInput(previous, plan.Matches);
            }

            if (inputUnchanged)
            {
                surface.RebuildFocusCandidates();
            }
            else
            {
                surface.InvalidateInput();
            }

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

            foreach ((Lumyte.Input.Abstractions.Key key, Widget owner) in _keys)
            {
                if (!surface._isDisposed && plan.Matches.TryGetValue(owner, out Widget? target) && target.CanRetainInputOnRebuild)
                {
                    surface._keyCaptures.Add(key, target);
                    continue;
                }

                try
                {
                    surface.DeliverInput(owner, new UiKeyCancelInput(key), false, true);
                }
                catch (Exception error)
                {
                    (failures ??= new()).Add(error);
                }
            }

            if (previousFocus is not null && nextFocus is null)
            {
                surface.NotifyFocus(previousFocus, false, ref failures);
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
