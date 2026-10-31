namespace Lumyte.UI.Windowing;

/// <summary>A borrowed owner whose release waits for popup and input cleanup.</summary>
public sealed class UiPopupOwner : IDisposable
{
    private Action? _release;

    internal UiPopupOwner(Action release)
    {
        _release = release;
    }

    /// <summary>Gets the nonreused owner identity.</summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>Gets a value indicating whether popup cleanup has finished.</summary>
    public bool IsReleased { get; internal set; }

    internal bool IsReleaseRequested { get; private set; }

    /// <summary>Stops admission and queues closure; does not dispose borrowed UI or windows.</summary>
    public void Dispose()
    {
        if (IsReleased)
        {
            _release = null;
            return;
        }

        IsReleaseRequested = true;
        Action? release = _release;
        _release = null;
        release?.Invoke();
    }
}
