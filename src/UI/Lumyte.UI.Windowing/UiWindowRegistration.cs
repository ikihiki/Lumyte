namespace Lumyte.UI.Windowing;

/// <summary>A borrowed root registration; dispose requests child and drawing drain.</summary>
public sealed class UiWindowRegistration : IDisposable
{
    private Action? _release;

    internal UiWindowRegistration(UiWindowId window, Action release)
    {
        Window = window;
        _release = release;
    }

    /// <summary>Gets the registered root identity.</summary>
    public UiWindowId Window { get; }

    /// <summary>Gets a value indicating whether all descendants, bindings and source loans have drained.</summary>
    public bool IsReleased { get; internal set; }

    /// <summary>Requests release without disposing the borrowed source.</summary>
    public void Dispose()
    {
        if (IsReleased)
        {
            _release = null;
            return;
        }

        Action? release = _release;
        _release = null;
        release?.Invoke();
    }
}
