namespace Lumyte.UI.Automation;

/// <summary>Resolves offscreen item keys only through an explicit input operation.</summary>
public sealed partial class UiLocator
{
    /// <summary>Reveals an owner-scoped item key under the ordinary exclusive input lease.</summary>
    /// <param name="options">The shared operation deadline.</param>
    /// <param name="cancellationToken">Cancels before dispatch.</param>
    /// <returns>The completed reveal, without selection or focus changes.</returns>
    public Task BringIntoViewAsync(UiWaitOptions? options = null, CancellationToken cancellationToken = default) => Page.ExecuteAsync(
        async run =>
        {
            if (!_queries.Any(query => query.ItemKey is not null))
            {
                throw run.Failure(UiAutomationFailureKind.UnsupportedAction, "Key reveal requires an item-key locator under its real owner.");
            }

            await PrepareItemTargetAsync(run);
            return true;
        },
        options,
        cancellationToken);

    private async ValueTask PrepareItemTargetAsync(UiAutomationRun run)
    {
        int index = Array.FindIndex(_queries, query => query.ItemKey is not null);
        if (index < 0)
        {
            return;
        }

        var owner = new UiLocator(Page, _queries[..index]);
        string key = _queries[index].ItemKey!;
        while (true)
        {
            run.Check();
            UiAutomationSnapshot current = await run.SnapshotAsync();
            UiAutomationNode? currentOwner = owner.Single(run, current);
            if (currentOwner is not null)
            {
                RequireAction(run, currentOwner, UiAutomationActions.SelectOption);
                UiAutomationNode? realized = current.Nodes.SingleOrDefault(node => node.SelectionGroup == currentOwner.Id && node.OptionKey == key);
                if (realized is not null && Actionable(realized, null, out _))
                {
                    return;
                }
            }

            if (await owner.RevealOptionAsync(run, key))
            {
                await run.NextFrameAsync("complete item range preparation after owner input");
                UiAutomationSnapshot snapshot = await run.SnapshotAsync();
                UiAutomationNode? group = owner.Single(run, snapshot);
                if (group is not null && snapshot.Nodes.Any(node => node.SelectionGroup == group.Id && node.OptionKey == key))
                {
                    return;
                }
            }

            await run.NextFrameAsync("item owner missing, occluded, disabled or key unavailable");
        }
    }
}
