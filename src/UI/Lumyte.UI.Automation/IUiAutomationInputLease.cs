namespace Lumyte.UI.Automation;

/// <summary>Owns one host's synthetic input until all its input is released or cancelled.</summary>
public interface IUiAutomationInputLease : IAsyncDisposable
{
    /// <summary>Gets the reserved, nonnegative pointer identity.</summary>
    long PointerId { get; }

    /// <summary>Tracks then dispatches this lease's input on the UI thread; validates the borrowed surface belongs to the host.</summary>
    /// <param name="surface">The surface borrowed inside RunOnUiThreadAsync.</param>
    /// <param name="input">Input owned by this lease; pending keys and pointer capture are retained even if Dispatch throws.</param>
    /// <returns>Whether normal dispatch consumed the input.</returns>
    bool Dispatch(UiSurface surface, UiInput input);
}
