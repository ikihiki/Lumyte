namespace Lumyte.UI.Windowing;

/// <summary>Observes one popup lifetime and queues idempotent closure.</summary>
public sealed class UiPopupHandle
{
    private Action? _close;

    internal UiPopupHandle(Action close)
    {
        _close = close;
    }

    /// <summary>Gets the nonreused popup identity.</summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>Gets the committed lifecycle state.</summary>
    public UiPopupState State { get; internal set; }

    /// <summary>Gets the selected presentation after preparation.</summary>
    public UiWindowPresentation? Presentation { get; internal set; }

    /// <summary>Gets the successful fallback reason, or empty.</summary>
    public string FallbackCode { get; internal set; } = string.Empty;

    /// <summary>Gets the terminal failure code, or empty.</summary>
    public string FailureCode { get; internal set; } = string.Empty;

    internal bool IsCloseRequested { get; private set; }

    /// <summary>Queues closure; safe from a content callback.</summary>
    public void Close()
    {
        IsCloseRequested = true;
        Action? close = _close;
        _close = null;
        if (State is not (UiPopupState.Closed or UiPopupState.Failed))
        {
            close?.Invoke();
        }
    }
}
