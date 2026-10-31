namespace Lumyte.UI.Windowing;

/// <summary>A drawing binding whose release waits for actual retirement.</summary>
public sealed class UiWindowBinding : IAsyncDisposable
{
    private Func<ValueTask>? _release;
    private Task? _completion;

    internal UiWindowBinding(UiWindowId window, Func<ValueTask> release)
    {
        Window = window;
        _release = release;
    }

    /// <summary>Gets the bound window identity.</summary>
    public UiWindowId Window { get; }

    /// <summary>Stops drawing and waits for GPU retirement; does not dispose the surface.</summary>
    /// <returns>The shared retirement completion.</returns>
    public ValueTask DisposeAsync()
    {
        if (_completion is null)
        {
            Func<ValueTask>? release = _release;
            _release = null;
            try
            {
                _completion = release!().AsTask();
            }
            catch (Exception error)
            {
                _completion = Task.FromException(error);
            }
        }

        return new(_completion);
    }
}
