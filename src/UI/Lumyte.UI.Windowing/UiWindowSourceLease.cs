namespace Lumyte.UI.Windowing;

/// <summary>A borrowed actual presentation root retained until this loan is returned.</summary>
public sealed class UiWindowSourceLease : IAsyncDisposable
{
    private Action? _release;
    private IUiWindowSource? _source;

    private bool _returned;

    internal UiWindowSourceLease(IUiWindowSource source, UiWindowId presentationRoot, UiWindowId keyboardRoot, Action release)
    {
        _source = source;
        PresentationRoot = presentationRoot;
        KeyboardRoot = keyboardRoot;
        _release = release;
    }

    /// <summary>Gets the borrowed source.</summary>
    public IUiWindowSource Source => _source ?? throw new ObjectDisposedException(nameof(UiWindowSourceLease));

    /// <summary>Gets the issuing manager's presentation root.</summary>
    public UiWindowId PresentationRoot { get; }

    /// <summary>Gets the keyboard root, which temporary windows preserve.</summary>
    public UiWindowId KeyboardRoot { get; }

    /// <summary>Returns the loan once, without disposing the source.</summary>
    /// <returns>A completed return.</returns>
    public ValueTask DisposeAsync()
    {
        if (!_returned)
        {
            _returned = true;
            Action? release = _release;
            _release = null;
            _source = null;
            release?.Invoke();
        }

        return ValueTask.CompletedTask;
    }
}
