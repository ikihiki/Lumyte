using System.Numerics;

namespace Lumyte.UI.Automation;

/// <summary>Closed desired-state operations dispatched through normal input.</summary>
public sealed partial class UiLocator
{
    /// <summary>Checks the target once, without input when already checked.</summary>
    /// <param name="options">Optional shared deadline.</param>
    /// <param name="cancellationToken">Cancels waiting.</param>
    /// <returns>The completed operation.</returns>
    public Task CheckAsync(UiWaitOptions? options = null, CancellationToken cancellationToken = default) => Page.ExecuteAsync(run => SetSelectionAsync(run, true, null), options, cancellationToken);

    /// <summary>Unchecks the target once, without input when already unchecked.</summary>
    /// <param name="options">Optional shared deadline.</param>
    /// <param name="cancellationToken">Cancels waiting.</param>
    /// <returns>The completed operation.</returns>
    public Task UncheckAsync(UiWaitOptions? options = null, CancellationToken cancellationToken = default) => Page.ExecuteAsync(run => SetSelectionAsync(run, false, null), options, cancellationToken);

    /// <summary>Selects an ordinal option key through the radio's ordinary pointer activation.</summary>
    /// <param name="optionKey">The nonempty option key.</param>
    /// <param name="options">Optional shared deadline.</param>
    /// <param name="cancellationToken">Cancels waiting.</param>
    /// <returns>The completed operation.</returns>
    public Task SelectOptionAsync(string optionKey, UiWaitOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(optionKey);
        return Page.ExecuteAsync(run => SetSelectionAsync(run, null, optionKey), options, cancellationToken);
    }

    private UiAutomationNode? SelectionTarget(UiAutomationRun run, UiAutomationSnapshot snapshot, bool? desired, string? key, out bool satisfied)
    {
        satisfied = false;
        UiAutomationNode? owner = Single(run, snapshot);
        if (owner is null)
        {
            return null;
        }

        RequireAction(run, owner, desired.HasValue ? UiAutomationActions.Check : UiAutomationActions.SelectOption);
        satisfied = desired.HasValue ? owner.IsChecked == desired : string.Equals(owner.SelectedKey, key, StringComparison.Ordinal);
        if (desired.HasValue || satisfied)
        {
            return owner;
        }

        if (owner.IsTransitioning || !owner.IsEnabled || !owner.IsVisible || !owner.HitTestVisible || owner.HasActivePress)
        {
            return null;
        }

        UiAutomationNode[] options = snapshot.Nodes.Where(node => node.SelectionGroup == owner.Id && string.Equals(node.OptionKey, key, StringComparison.Ordinal)).ToArray();
        if (options.Length > 1)
        {
            throw run.Failure(UiAutomationFailureKind.AmbiguousMatch, "The group contains multiple matching option keys.");
        }

        UiAutomationNode? target = options.SingleOrDefault();
        if (target is not null)
        {
            RequireAction(run, target, UiAutomationActions.Click);
        }

        return target;
    }

    private async ValueTask<bool> SetSelectionAsync(UiAutomationRun run, bool? desired, string? key)
    {
        await PrepareItemTargetAsync(run);
        UiAutomationNode? previous = null;
        while (true)
        {
            UiAutomationSnapshot observed = await run.SnapshotAsync();
            UiAutomationNode? node = SelectionTarget(run, observed, desired, key, out bool satisfied);
            if (node is null && !desired.HasValue && key is not null)
            {
                await RevealOptionAsync(run, key);
            }

            if (satisfied)
            {
                return true;
            }

            if (node is not null && previous is not null && SameGeometry(previous, node) && Actionable(node, null, out Vector2 candidate)
                && await run.Host.RunOnUiThreadAsync(surface => run.Probe(surface, candidate).ActivationTarget == node.Id, run.Token))
            {
                bool sent = await run.WithInputAsync(async lease => await run.Host.RunOnUiThreadAsync(
                surface =>
                {
                    run.Check();
                    UiAutomationSnapshot snapshot = surface.CaptureAutomationSnapshot();
                    run.Remember(snapshot);
                    UiAutomationNode? current = SelectionTarget(run, snapshot, desired, key, out bool done);
                    if (done)
                    {
                        return true;
                    }

                    if (current is null || !SameGeometry(node, current) || !Actionable(current, null, out Vector2 point)
                        || run.Probe(surface, point).ActivationTarget != current.Id)
                    {
                        return false;
                    }

                    run.Send(lease, surface, new UiPointerInput(lease.PointerId, UiPointerPhase.Move, point));
                    UiAutomationSnapshot moved = surface.CaptureAutomationSnapshot();
                    run.Remember(moved);
                    UiAutomationNode? afterMove = SelectionTarget(run, moved, desired, key, out bool nowDone);
                    if (nowDone)
                    {
                        return true;
                    }

                    if (afterMove is null || !SameGeometry(current, afterMove) || !Actionable(afterMove, null, out Vector2 movedPoint)
                        || movedPoint != point || run.Probe(surface, point).ActivationTarget != current.Id)
                    {
                        throw run.Failure(UiAutomationFailureKind.TargetChanged, "The target changed after pointer Move; input will not be retried.");
                    }

                    if (desired is { } value)
                    {
                        if (!run.Send(lease, surface, new UiCheckInput(current.Id, value, point)))
                        {
                            throw run.Failure(UiAutomationFailureKind.ActionFailed, "Desired-state dispatch was rejected; input will not be retried.");
                        }
                    }
                    else
                    {
                        run.Send(lease, surface, new UiPointerInput(lease.PointerId, UiPointerPhase.Down, point));
                        run.Send(lease, surface, new UiPointerInput(lease.PointerId, UiPointerPhase.Up, point));
                    }

                    return true;
                },
                run.Token));
                if (sent)
                {
                    return true;
                }
            }

            previous = node;
            await run.NextFrameAsync("missing, unstable, disabled, pressed, clipped or occluded selection target");
        }
    }

    private ValueTask<bool> RevealOptionAsync(UiAutomationRun run, string key) => run.WithInputAsync(async lease => await run.Host.RunOnUiThreadAsync(
        surface =>
        {
            run.Check();
            UiAutomationSnapshot snapshot = surface.CaptureAutomationSnapshot();
            run.Remember(snapshot);
            UiAutomationNode? owner = Single(run, snapshot);
            if (owner is null || !Actionable(owner, null, out Vector2 point))
            {
                return false;
            }

            RequireAction(run, owner, UiAutomationActions.SelectOption);
            UiAutomationId? hit = run.Host is IUiAutomationRoutingHost routing ? routing.ProbeWheel(surface, point) : surface.ProbeWheel(point);
            UiAutomationNode? destination = snapshot.Nodes.FirstOrDefault(node => node.Id == hit);
            while (destination is not null && destination.Id != owner.Id)
            {
                destination = snapshot.Nodes.FirstOrDefault(node => node.Id == destination.ParentId);
            }

            return destination is not null && run.Send(lease, surface, new UiBringItemInput(owner.Id, key, point));
        },
        run.Token));
}
