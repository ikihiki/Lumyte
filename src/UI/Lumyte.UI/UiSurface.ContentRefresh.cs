using Widget = Lumyte.UI.Compose.Definitions.Widget;

namespace Lumyte.UI;

/// <summary>Owns persistent UI instances with caller-managed synchronization.</summary>
public sealed partial class UiSurface
{
    private readonly Dictionary<UiElement, (Widget Configuration, long Version)> _contentRefreshes = new(ReferenceEqualityComparer.Instance);
    private bool _processingContentRefreshes;
    private long _contentRefreshVersion;

    internal void QueueContentRefresh(UiElement element)
    {
        if (!_isDisposed && ReferenceEquals(element.Surface, this))
        {
            _contentRefreshes[element] = (element.Widget, ++_contentRefreshVersion);
        }
    }

    internal void ForgetContentRefresh(UiElement element) => _contentRefreshes.Remove(element);

    private void ClearContentRefreshes() => _contentRefreshes.Clear();

    private void ProcessContentRefreshes()
    {
        if (_processingContentRefreshes || _applyingRebuild || _dispatchDepth != 0 || _recordDepth != 0 || _layoutPass is not null)
        {
            return;
        }

        _processingContentRefreshes = true;
        try
        {
            // Requests raised during preparation/publication belong to a later update.
            foreach ((UiElement element, (Widget Configuration, long Version) request) in _contentRefreshes.ToArray())
            {
                if (!_contentRefreshes.TryGetValue(element, out (Widget Configuration, long Version) current) || current != request)
                {
                    continue;
                }

                _contentRefreshes.Remove(element);
                Widget configuration = request.Configuration;
                if (!ReferenceEquals(element.Surface, this) || !ReferenceEquals(element.Widget, configuration))
                {
                    continue;
                }

                try
                {
                    RefreshElementContent(element);
                }
                catch
                {
                    if (ReferenceEquals(element.Surface, this) && !_isDisposed)
                    {
                        _contentRefreshes.TryAdd(element, request);
                    }

                    throw;
                }
            }
        }
        finally
        {
            _processingContentRefreshes = false;
        }
    }
}
