namespace Lumyte.UI.WebView;

// Both callback and owner readers account against the same application payload budget.
internal sealed class WebViewPixelBudget(long limit)
{
    private readonly object _gate = new();
    private long _used;

    internal long Limit => limit;

    internal long Used => Interlocked.Read(ref _used);

    internal bool TryReserve(long bytes)
    {
        lock (_gate)
        {
            if (bytes <= 0 || bytes > limit - _used)
            {
                return false;
            }

            _used += bytes;
            return true;
        }
    }

    internal void Release(long bytes)
    {
        lock (_gate)
        {
            _used -= bytes;
        }
    }
}
