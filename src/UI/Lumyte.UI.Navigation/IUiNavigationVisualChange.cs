namespace Lumyte.UI.Navigation;

/// <summary>An optional staged host capability; ordinary page consumers do not implement this interface.</summary>
public interface IUiNavigationVisualChange
{
    /// <summary>Prepares bounded optional images after quiescence and before publication.</summary>
    /// <param name="transitions">The optional immutable definitions.</param>
    /// <param name="skipRequested">The thread-safe skip intention.</param>
    /// <param name="cancellationToken">Precommit cancellation.</param>
    /// <returns>Preparation completion.</returns>
    ValueTask PrepareVisualsAsync(UiPageTransitions? transitions, Func<bool> skipRequested, CancellationToken cancellationToken);

    /// <summary>Waits for final ordinary presentation independently of retirement.</summary>
    /// <returns>The visual result.</returns>
    ValueTask<UiVisualTransitionResult> RunVisualsAsync();

    /// <summary>Stops optional work during host shutdown without awaiting presentation.</summary>
    void StopVisuals();
}
