namespace Lumyte.Diagnostics.UI.Automation.Client;

/// <summary>Assertions retried on the game within one logical job.</summary>
public sealed class UiRemoteExpect
{
    private readonly UiRemoteLocator _locator;

    internal UiRemoteExpect(UiRemoteLocator locator)
    {
        _locator = locator;
    }

    /// <summary>Waits for the game-side visible predicate.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The completed assertion.</returns>
    public async Task ToBeVisibleAsync(CancellationToken cancellationToken = default) => await _locator.AssertAsync("visible", null, cancellationToken);

    /// <summary>Waits for the game-side hidden predicate.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The completed assertion.</returns>
    public async Task ToBeHiddenAsync(CancellationToken cancellationToken = default) => await _locator.AssertAsync("hidden", null, cancellationToken);

    /// <summary>Waits for the game-side enabled predicate.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The completed assertion.</returns>
    public async Task ToBeEnabledAsync(CancellationToken cancellationToken = default) => await _locator.AssertAsync("enabled", null, cancellationToken);

    /// <summary>Waits for the game-side focused predicate.</summary>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The completed assertion.</returns>
    public async Task ToBeFocusedAsync(CancellationToken cancellationToken = default) => await _locator.AssertAsync("focused", null, cancellationToken);

    /// <summary>Waits for an exact nonnegative count.</summary>
    /// <param name="count">The expected count.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>The completed assertion.</returns>
    public async Task ToHaveCountAsync(int count, CancellationToken cancellationToken = default) => await _locator.AssertAsync("count", count, cancellationToken);
}
