using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>Prepares observer ownership before publishing configurations.</summary>
public sealed partial class UiSurface
{
    private void PrepareSignalUpdates(RebuildPlan plan)
    {
        foreach (ElementUpdate update in plan.Updates)
        {
            if (!ReferenceEquals(update.Previous, update.Next)
                && (update.Element.Signals is not null || update.Next.SignalArguments is { Count: > 0 } || update.Next.ConstructionReads is { IsEmpty: false }))
            {
                update.Signals = new(this, update.Element, update.Next);
            }
        }
    }

    private sealed class SignalUpdate : IDisposable
    {
        private readonly UiSurface _surface;
        private readonly UiElement _element;
        private readonly Widget _next;
        private readonly UiElement.SignalObserver? _observer;
        private readonly bool _borrowed;
        private bool _committed;

        internal SignalUpdate(UiSurface surface, UiElement element, Widget next)
        {
            _surface = surface;
            _element = element;
            _next = next;
            if (next.SignalArguments is { Count: > 0 } || next.ConstructionReads is { IsEmpty: false })
            {
                if (element.Signals is { } current && current.CanReuse(next))
                {
                    _observer = current;
                    _borrowed = true;
                }
                else
                {
                    _observer = new(surface, element, next);
                }
            }
        }

        public void Dispose()
        {
            if (!_committed && !_borrowed)
            {
                _observer?.Dispose();
            }
        }

        internal void Commit()
        {
            UiElement.SignalObserver? previous = _element.Signals;
            _element.Signals = _observer;
            _committed = true;
            if (!ReferenceEquals(previous, _observer))
            {
                _surface._signalConnections = checked(_surface._signalConnections + 1);
                previous?.Dispose();
            }

            _observer?.Adopt(_next);
        }
    }
}
