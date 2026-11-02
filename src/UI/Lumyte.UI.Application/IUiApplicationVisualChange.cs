namespace Lumyte.UI.Application;

/// <summary>Internal presentation capability shared with the optional navigation adapter.</summary>
internal interface IUiApplicationVisualChange
{
    /// <summary>Prepares optional images after input quiescence and before synchronous publication.</summary>
    /// <param name="plan">The optional value-only transition plan.</param>
    /// <param name="skip">The thread-safe skip intention.</param>
    /// <param name="token">Precommit cancellation.</param>
    /// <returns>Preparation completion.</returns>
    ValueTask PrepareVisualsAsync(UiViewVisualPlan? plan, Func<bool> skip, CancellationToken token);

    /// <summary>Waits for final ordinary presentation independently of GPU retirement.</summary>
    /// <returns>The closed visual outcome.</returns>
    ValueTask<UiViewVisualOutcome> RunVisualsAsync();

    /// <summary>Stops waiting for presentation on host shutdown.</summary>
    void StopVisuals();
}
