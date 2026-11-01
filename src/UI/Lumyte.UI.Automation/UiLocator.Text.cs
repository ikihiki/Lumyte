namespace Lumyte.UI.Automation;

/// <summary>Defines ui locator.</summary>
public sealed partial class UiLocator
{
    /// <summary>Fill Async.</summary>
    /// <param name = "text">The text.</param>
    /// <param name = "options">The options.</param>
    /// <param name = "cancellationToken">The cancellation token.</param>
    /// <returns>The operation result.</returns>
    public Task FillAsync(string text, UiWaitOptions? options = null, CancellationToken cancellationToken = default)
    {
        UiAutomationTextValidation.Validate(text);
        return Page.ExecuteAsync(
 async run =>
        {
            if (run.Host is not IUiAutomationTextHost editing)
            {
                throw run.Failure(UiAutomationFailureKind.UnsupportedAction, "The host does not provide committed text editing.");
            }

            return await run.WithInputAsync(
 async lease =>
            {
                UiAutomationId focused = await FocusAsync(run, lease);
                return await run.Host.RunOnUiThreadAsync(
 surface =>
                {
                    run.Check();
                    UiAutomationSnapshot snapshot = surface.CaptureAutomationSnapshot();
                    run.Remember(snapshot);
                    UiAutomationNode? node = Single(run, snapshot);
                    if (node is null || node.Id != focused || !node.IsFocused || !Visible(node) || !node.IsEnabled || !run.CanReceiveKeys(surface))
                    {
                        throw run.Failure(UiAutomationFailureKind.TargetChanged, "The editing target changed before input; the edit was not sent.");
                    }

                    RequireAction(run, node, UiAutomationActions.Edit);
                    editing.Fill(surface, node.Id, text);
                    return true;
                },
 run.Token);
            },
 editing.AcquireTextInputAsync);
        },
 options,
 cancellationToken);
    }
}
