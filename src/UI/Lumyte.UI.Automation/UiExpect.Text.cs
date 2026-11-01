namespace Lumyte.UI.Automation;

/// <summary>Defines ui expect.</summary>
public sealed partial class UiExpect
{
    /// <summary>To Have Value Async.</summary>
    /// <param name = "text">The text.</param>
    /// <param name = "options">The options.</param>
    /// <param name = "cancellationToken">The cancellation token.</param>
    /// <returns>The operation result.</returns>
    public Task ToHaveValueAsync(string text, UiWaitOptions? options = null, CancellationToken cancellationToken = default)
    {
        UiAutomationTextValidation.Validate(text);
        return _locator.Page.ExecuteAsync(
 async run =>
        {
            if (run.Host is not IUiAutomationTextHost editing)
            {
                throw run.Failure(UiAutomationFailureKind.UnsupportedAction, "The host does not provide text inspection.");
            }

            while (true)
            {
                bool equal = await run.Host.RunOnUiThreadAsync(
 surface =>
                {
                    UiAutomationSnapshot snapshot = surface.CaptureAutomationSnapshot();
                    run.Remember(snapshot);
                    UiAutomationNode? node = _locator.Single(run, snapshot);
                    return node is not null && string.Equals(editing.ReadValue(surface, node.Id), text, StringComparison.Ordinal);
                },
 run.Token);
                if (equal)
                {
                    return true;
                }

                await run.NextFrameAsync("committed text value condition");
            }
        },
 options,
 cancellationToken);
    }
}
