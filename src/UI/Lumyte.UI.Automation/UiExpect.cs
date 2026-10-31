namespace Lumyte.UI.Automation;

/// <summary>Provides strict, frame-driven assertions that share the page operation deadline.</summary>
public sealed class UiExpect
{
    private readonly UiLocator _locator;

    internal UiExpect(UiLocator locator)
    {
        _locator = locator;
    }

    /// <summary>Waits for effective visibility and positive unclipped bounds; occlusion is not checked.</summary>
    /// <param name="options">Optional shared deadline.</param>
    /// <param name="cancellationToken">Cancels waiting.</param>
    /// <returns>The successful assertion.</returns>
    public Task ToBeVisibleAsync(UiWaitOptions? options = null, CancellationToken cancellationToken = default) => WaitAsync(node => node is not null && Visible(node), options, cancellationToken);

    /// <summary>Waits for no matches or one hidden or zero-area match.</summary>
    /// <param name="options">Optional shared deadline.</param>
    /// <param name="cancellationToken">Cancels waiting.</param>
    /// <returns>The successful assertion.</returns>
    public Task ToBeHiddenAsync(UiWaitOptions? options = null, CancellationToken cancellationToken = default) => WaitAsync(node => node is null || !Visible(node), options, cancellationToken);

    /// <summary>Waits for one element enabled through all its ancestors.</summary>
    /// <param name="options">Optional shared deadline.</param>
    /// <param name="cancellationToken">Cancels waiting.</param>
    /// <returns>The successful assertion.</returns>
    public Task ToBeEnabledAsync(UiWaitOptions? options = null, CancellationToken cancellationToken = default) => WaitAsync(node => node?.IsEnabled == true, options, cancellationToken);

    /// <summary>Waits for one element with ordinary surface-local focus.</summary>
    /// <param name="options">Optional shared deadline.</param>
    /// <param name="cancellationToken">Cancels waiting.</param>
    /// <returns>The successful assertion.</returns>
    public Task ToBeFocusedAsync(UiWaitOptions? options = null, CancellationToken cancellationToken = default) => WaitAsync(node => node?.IsFocused == true, options, cancellationToken);

    /// <summary>Waits for an exact count, allowing zero or multiple matches.</summary>
    /// <param name="count">The nonnegative expected count.</param>
    /// <param name="options">Optional shared deadline.</param>
    /// <param name="cancellationToken">Cancels waiting.</param>
    /// <returns>The successful assertion.</returns>
    public Task ToHaveCountAsync(int count, UiWaitOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        return _locator.Page.ExecuteAsync(
            async run =>
            {
                run.Trace(_locator.Description);
                while (_locator.Resolve(await run.SnapshotAsync()).Count != count)
                {
                    await run.NextFrameAsync($"expected count {count}");
                }

                return true;
            },
            options,
            cancellationToken);
    }

    private static bool Visible(UiAutomationNode node) => node.IsVisible && node.Bounds.Width > 0 && node.Bounds.Height > 0;

    private Task WaitAsync(Func<UiAutomationNode?, bool> predicate, UiWaitOptions? options, CancellationToken token) => _locator.Page.ExecuteAsync(
        async run =>
        {
            while (!predicate(_locator.Single(run, await run.SnapshotAsync())))
            {
                await run.NextFrameAsync("assertion not satisfied");
            }

            return true;
        },
        options,
        token);
}
