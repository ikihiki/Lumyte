namespace Lumyte.UI.Automation;

/// <summary>Defines iui automation text host.</summary>
public interface IUiAutomationTextHost
{
    /// <summary>Acquire Text Input Async.</summary>
    /// <param name = "cancellationToken">The cancellation token.</param>
    /// <returns>The operation result.</returns>
    ValueTask<IUiAutomationInputLease> AcquireTextInputAsync(CancellationToken cancellationToken = default);

    /// <summary>Fill.</summary>
    /// <param name = "surface">The surface.</param>
    /// <param name = "editor">The editor.</param>
    /// <param name = "text">The text.</param>
    void Fill(UiSurface surface, UiAutomationId editor, string text);

    /// <summary>Read Value.</summary>
    /// <param name = "surface">The surface.</param>
    /// <param name = "editor">The editor.</param>
    /// <returns>The operation result.</returns>
    string ReadValue(UiSurface surface, UiAutomationId editor);
}
