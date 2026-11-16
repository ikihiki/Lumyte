namespace Lumyte.UI.WebView.Cef;

/// <summary>Reconciles latest owner focus with a newly available native host.</summary>
internal sealed class CefFocusAdmission
{
    private bool? _applied;

    internal bool Focused { get; private set; }

    internal long Request { get; private set; }

    internal void Set(bool focused, long request)
    {
        if (request < Request)
        {
            return;
        }

        Focused = focused;
        Request = request;
    }

    internal void Apply(bool closing, Action<bool> setFocus, Action cancelCapture)
    {
        bool focused = !closing && Focused;
        if (_applied == focused)
        {
            return;
        }

        _applied = focused;
        setFocus(focused);
        if (!focused)
        {
            cancelCapture();
        }
    }
}
